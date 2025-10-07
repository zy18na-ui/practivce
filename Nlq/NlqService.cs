using dataAccess.Planning.Validation;
using dataAccess.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Planning.Nlq;

public sealed class NlqService
{
    private readonly ISqlCatalog _catalog;
    private readonly TimeResolver _time;
    private readonly CapabilityGuard _guard;
    private readonly MetricMapper _map;
    private readonly AnswerFormatter _fmt;
    private readonly PlanValidator _validator;

    public NlqService(
        ISqlCatalog catalog,
        TimeResolver time,
        CapabilityGuard guard,
        MetricMapper map,
        AnswerFormatter fmt,
        PlanValidator validator)
    {
        _catalog = catalog;
        _time = time;
        _guard = guard;
        _map = map;
        _fmt = fmt;
        _validator = validator;
    }

    public async Task<object> HandleAsync(string text, CancellationToken ct = default)
    {
        var plan = DraftPlanFromText(text ?? string.Empty);
        var resolved = ResolveTimes(plan);

        var (allowed, msg) = _guard.Check(resolved);
        if (!allowed)
            return new { mode = "chat", markdown = msg };

        if (string.Equals(resolved.Mode, "report", StringComparison.OrdinalIgnoreCase))
        {
            var ui = await BuildReportUiAsync(text ?? "", resolved, ct);
            return ui;
        }

        var answer = await BuildAnswerAsync(text ?? "", resolved, ct);
        return answer;
    }

    private static readonly Regex DateRx =
    new(@"\b(\d{4})-(\d{2})-(\d{2})\b|(?:\b|^)([A-Za-z]{3,9})\s+(\d{1,2}),\s*(\d{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MonthRx =
        new(@"\b(january|february|march|april|may|june|july|august|september|october|november|december)\b(?:\s+(\d{4}))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MonthAbbrRx =
        new(@"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)\b(?:\s+(\d{4}))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex QuarterRx =
        new(@"\bq([1-4])\b(?:\s+(\d{4}))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LastNDaysRx =
        new(@"\blast\s+(\d{1,3})\s+days?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BetweenDatesRx =
        new(@"\bbetween\s+(\d{4}-\d{2}-\d{2})\s+and\s+(\d{4}-\d{2}-\d{2})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LastNWeeksRx =
        new(@"\blast\s+(\d{1,2})\s+weeks?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex LastNMonthsRx =
        new(@"\blast\s+(\d{1,2})\s+months?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static NlqPlan DraftPlanFromText(string text)
    {
        var t = (text ?? string.Empty).Trim();
        var lc = t.ToLowerInvariant();

        // ---- mode & compare flags
        bool isReport = lc.Contains("report") || lc.Contains("dashboard")
                        || lc.StartsWith("create ") || lc.StartsWith("make ") || lc.StartsWith("show ");
        bool wantsCompare = lc.Contains(" compare ") || lc.Contains("compare ")
                            || lc.Contains(" vs ") || lc.Contains(" versus ");

        // ---- domain guess (add sell/selling hints for sales)
        string domain =
            (lc.Contains("inventor") || lc.Contains("stock")) ? "inventory" :
            (lc.Contains("sale") || lc.Contains("sales") || lc.Contains("revenue") ||
             lc.Contains("orders") || lc.Contains("units") ||
             lc.Contains("sell") || lc.Contains("selling") || lc.Contains("seller"))
            ? "sales"
            : (lc.Contains("expense") || lc.Contains("spend") || lc.Contains("cost")) ? "expense"
            : "";

        // If domain not detected but clearly talks inventory → assume inventory
        if (string.IsNullOrEmpty(domain) &&
            (lc.Contains("available products") || lc.Contains("available items") ||
             lc.Contains("in stock") || lc.Contains("on hand") ||
             lc.Contains("low stock") || lc.Contains("out of stock") || lc.Contains("oos")))
        {
            domain = "inventory";
        }

        // ---- metric base
        string? metric = null;
        if (domain == "expense") metric = "total_expense";
        if (domain == "inventory") metric ??= "summary";

        // ---- SALES: breakdowns / series overrides (kept in answer-mode)
        if (domain == "sales")
        {
            var mentionsByCategory = lc.Contains("by category") || lc.Contains("per category") || (lc.Contains("category") && lc.Contains(" by "));
            var mentionsColor = lc.Contains("by color") || lc.Contains(" colour ") || lc.EndsWith(" color") || lc.Contains(" color ");
            var mentionsAgeSize = lc.Contains("by size") || lc.Contains(" age size") || lc.Contains(" agesize") || lc.EndsWith(" size");
            var mentionsByProduct = lc.Contains("by product") || lc.Contains("per product");

            var mentionsByDay = lc.Contains("by day") || lc.Contains("daily");
            var mentionsByWeek = lc.Contains("by week") || lc.Contains("weekly");
            var mentionsByHour = lc.Contains("by hour") || lc.Contains("hourly");

            if (mentionsColor) metric = "by_category_color";
            else if (mentionsAgeSize) metric = "by_category_agesize";
            else if (mentionsByCategory) metric = "by_category";
            else if (mentionsByProduct) metric = "by_product";
            else if (mentionsByHour) metric = "by_hour";
            else if (mentionsByWeek) metric = "by_week";
            else if (mentionsByDay) metric = "by_day";

            if (mentionsByCategory || mentionsColor || mentionsAgeSize || mentionsByProduct ||
                mentionsByDay || mentionsByWeek || mentionsByHour)
                isReport = false;
        }

        // ---- INVENTORY: pick a concrete metric if we forced inventory
        // ---- INVENTORY: pick a concrete metric + force answer-mode for list asks
        if (domain == "inventory")
        {
            if (lc.Contains("available products") || lc.Contains("available items") ||
                lc.Contains("in stock") || lc.Contains("on hand"))
            {
                metric = "available";
                isReport = false;
            }
            else if (lc.Contains("low stock"))
            {
                metric = "low_stock";
                isReport = false;
            }
            else if (lc.Contains("out of stock") || lc.Contains("oos"))
            {
                metric = "out_of_stock";
                isReport = false;
            }
            else if (lc.Contains("by product"))
            {
                metric = "by_product";
                isReport = false;
            }
            else
            {
                metric ??= "summary";
            }
        }

        // ---- time preset detection (extended)
        string preset;
        if (lc.Contains("today")) preset = "today";
        else if (lc.Contains("yesterday")) preset = "yesterday";
        else if (lc.Contains("current")) preset = "today";     // “current” → today
        else if (lc.Contains("this week")) preset = "this_week";
        else if (lc.Contains("last week")) preset = "last_week";
        else if (lc.Contains("last 7 days")) preset = "last_7_days";
        else if (lc.Contains("last 30 days")) preset = "last_30_days";
        else if (lc.Contains("this month")) preset = "this_month";
        else if (lc.Contains("last month")) preset = "last_month";
        else if (lc.Contains("this quarter")) preset = "this_quarter";
        else if (lc.Contains("last quarter")) preset = "last_quarter";
        else if (lc.Contains("this year")) preset = "this_year";
        else if (lc.Contains("last year")) preset = "last_year";
        else if (lc.Contains("ytd") || lc.Contains("year to date")) preset = "ytd";
        else if (lc.Contains("as of")) preset = "as_of";
        else preset = (domain == "inventory") ? "as_of" : "this_week";

        // Inventory is a point-in-time snapshot → coerce non-as_of to as_of
        if (domain == "inventory" && preset != "as_of")
            preset = "as_of";

        // ---- convert textual periods to explicit ranges when present
        string? startStr = null;
        string? endStr = null;

        // Full month: “September [2025]”
        var mm = MonthRx.Match(t);
        if (mm.Success)
        {
            var monthName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mm.Groups[1].Value.ToLowerInvariant());
            int month = DateTime.ParseExact(monthName, "MMMM", CultureInfo.InvariantCulture).Month;
            int year = mm.Groups[2].Success ? int.Parse(mm.Groups[2].Value, CultureInfo.InvariantCulture)
                                             : DateTime.UtcNow.Year;

            var first = new DateOnly(year, month, 1);
            var last = first.AddMonths(1).AddDays(-1);

            preset = "range";
            startStr = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            endStr = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // Short month: “Sep / Sept [2025]”
        if (startStr is null && endStr is null)
        {
            var ma = MonthAbbrRx.Match(t);
            if (ma.Success)
            {
                string abbr = ma.Groups[1].Value.ToLowerInvariant();
                int month = abbr switch
                {
                    "jan" => 1,
                    "feb" => 2,
                    "mar" => 3,
                    "apr" => 4,
                    "may" => 5,
                    "jun" => 6,
                    "jul" => 7,
                    "aug" => 8,
                    "sep" or "sept" => 9,
                    "oct" => 10,
                    "nov" => 11,
                    "dec" => 12,
                    _ => 1
                };
                int year = ma.Groups[2].Success ? int.Parse(ma.Groups[2].Value, CultureInfo.InvariantCulture)
                                                 : DateTime.UtcNow.Year;

                var first = new DateOnly(year, month, 1);
                var last = first.AddMonths(1).AddDays(-1);
                preset = "range";
                startStr = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                endStr = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        // Quarter: “Q3 [2025]”
        if (startStr is null && endStr is null)
        {
            var qx = QuarterRx.Match(t);
            if (qx.Success)
            {
                int q = int.Parse(qx.Groups[1].Value, CultureInfo.InvariantCulture);
                int year = qx.Groups[2].Success ? int.Parse(qx.Groups[2].Value, CultureInfo.InvariantCulture)
                                                : DateTime.UtcNow.Year;

                int startMonth = 1 + (q - 1) * 3;
                var first = new DateOnly(year, startMonth, 1);
                var last = first.AddMonths(3).AddDays(-1);
                preset = "range";
                startStr = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                endStr = last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        // Rolling window: “last N days”
        if (startStr is null && endStr is null)
        {
            var ln = LastNDaysRx.Match(t);
            if (ln.Success)
            {
                int n = Math.Clamp(int.Parse(ln.Groups[1].Value, CultureInfo.InvariantCulture), 1, 365);
                var today = DateTime.UtcNow.Date;
                var start = today.AddDays(-(n - 1));
                var end = today;
                preset = "range";
                startStr = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                endStr = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        // Explicit range: “between 2025-09-01 and 2025-09-15”
        if (startStr is null && endStr is null)
        {
            var bt = BetweenDatesRx.Match(t);
            if (bt.Success)
            {
                preset = "range";
                startStr = bt.Groups[1].Value;
                endStr = bt.Groups[2].Value;
            }
        }

        // “as of <date>”
        string? asOfEnd = null;
        if (preset == "as_of" && startStr is null && endStr is null)
        {
            var m = DateRx.Match(t);
            if (m.Success)
            {
                if (m.Groups[1].Success)
                    asOfEnd = $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}";
                else
                {
                    var monthName = m.Groups[4].Value;
                    var day = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
                    var year = int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture);
                    var month = DateTime.ParseExact(monthName, "MMMM", CultureInfo.InvariantCulture,
                                                    DateTimeStyles.AllowWhiteSpaces).Month;
                    asOfEnd = new DateOnly(year, month, day).ToString("yyyy-MM-dd");
                }
            }
            // else: leave null → TimeResolver(as_of) treats null as today
        }

        // --- OPTIONAL: pull "top N" and a simple name filter from text
        int? kFromText = null;
        var mTop = Regex.Match(lc, @"\btop\s+(\d{1,3})\b");
        if (mTop.Success && int.TryParse(mTop.Groups[1].Value, out var kVal))
            kFromText = Math.Clamp(kVal, 1, 500);

        // Very simple “name contains” heuristic for inventory lists
        string? nameLike = null;
        if (domain == "inventory" && (metric == "available" || metric == "by_product"))
        {
            // e.g., "available products unicorn"
            var mName = Regex.Match(lc, @"\b(?:products?|items?)\s+([a-z0-9\-\s]{3,})$");
            if (mName.Success)
                nameLike = mName.Groups[1].Value.Trim();
        }

        // Build the entities dict only with non-null values
        var entities = new Dictionary<string, object?>();
        if (kFromText is not null) entities["k"] = kFromText;
        if (!string.IsNullOrWhiteSpace(nameLike)) entities["name_like"] = nameLike;
        if (entities.Count == 0) entities = null!;

        return new NlqPlan
        {
            Mode = isReport ? "report" : "answer",
            Domains = string.IsNullOrEmpty(domain) ? new() : new() { domain },
            Metric = metric,
            Time = new TimeSpec { Preset = preset, Start = startStr, End = endStr ?? asOfEnd },
            CompareToPrior = wantsCompare && domain is "expense" or "sales",
            Confidence = 0.9,
            Entities = entities
        };
    }

    private NlqResolvedPlan ResolveTimes(NlqPlan p)
    {
        var (start, end) = _time.ResolveRange(p.Time);

        DateOnly? ps = null, pe = null;
        if (p.CompareToPrior && p.Domains.Count == 1 && (p.Domains[0] is "expense" or "sales"))
        {
            var pr = _time.PriorWindow(start, end);
            ps = pr.ps; pe = pr.pe;
        }

        return new NlqResolvedPlan
        {
            Mode = p.Mode,
            Domains = p.Domains,
            Metric = p.Metric,
            Time = p.Time,
            Filters = p.Filters,
            CompareToPrior = p.CompareToPrior,
            Confidence = p.Confidence,
            Start = start,
            End = end,
            PriorStart = ps,
            PriorEnd = pe
        };
    }

    private async Task<string> BuildAnswerAsync(string userText, NlqResolvedPlan plan, CancellationToken ct)
    {
        // Chit-chat / no domain → reply without hitting the mapper
        if (string.IsNullOrEmpty(plan.Domain))
            return "Hi! I’m **BuiswAIz** — ask me about expense, sales, or inventory.";

        var (qid, args, kind) = _map.GetAnswerQuery(plan);
        var result = await _catalog.RunAsync(qid, args, ct);

        // Common label (includes today/yesterday properly)
        string label = plan.Time.Preset switch
        {
            "today" => "today",
            "yesterday" => "yesterday",
            "this_week" => "this week",
            "last_week" => "last week",
            "this_month" => "this month",
            "last_month" => "last month",
            "as_of" => $"as of {plan.End:yyyy-MM-dd}",
            "range" => $"{plan.Start:yyyy-MM-dd}–{plan.End:yyyy-MM-dd}",
            _ => $"{plan.Start:yyyy-MM-dd}–{plan.End:yyyy-MM-dd}"
        };

        // ---- EXPENSE / SALES keep existing behavior via AnswerFormatter ----
        var domain = plan.Domain.ToLowerInvariant();
        if (domain == "expense")
        {
            var s = new AnswerFormatter().ExpenseSummaryOneLiner(result!, label);
            return CleanNlqMarkdown(s);
        }

        if (domain == "sales")
        {
            if (plan.Metric == "top_product")
            {
                var s = new AnswerFormatter().SalesTopProductOneLiner(result, label);
                return CleanNlqMarkdown(s);
            }
            else
            {
                var s = new AnswerFormatter().SalesSummaryOneLiner(result!, label, plan.Metric ?? "revenue");
                return CleanNlqMarkdown(s);
            }
        }

        // ---- INVENTORY: support answer-mode intents (NO MORE “report-only”) ----
        if (domain == "inventory")
        {
            var metric = (plan.Metric ?? "snapshot").ToLowerInvariant();

            // small helpers
            static int Count(object? seq)
            {
                if (seq is System.Collections.IEnumerable e) { int c = 0; foreach (var _ in e) c++; return c; }
                return 0;
            }
            static string? GetProp(object o, string name)
            {
                var p = o.GetType().GetProperty(name,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.IgnoreCase);
                var v = p?.GetValue(o);
                return v?.ToString();
            }

            switch (metric)
            {
                case "available":
                    {
                        // result = IEnumerable of { product_name, current_stock, ... }
                        var n = Count(result);
                        if (n == 0) return "No **available products** right now.";
                        // Build a short preview (first 10 lines)
                        var lines = new List<string>();
                        if (result is System.Collections.IEnumerable e)
                        {
                            int i = 0;
                            foreach (var row in e)
                            {
                                var name = GetProp(row!, "product_name") ?? GetProp(row!, "ProductName") ?? "(unknown)";
                                var stock = GetProp(row!, "current_stock") ?? GetProp(row!, "CurrentStock") ?? "?";
                                lines.Add($"- {name} — {stock}");
                                if (++i >= 10) break;
                            }
                        }
                        return $"Available products: **{n}**\n\n" + string.Join("\n", lines);
                    }

                case "by_product":
                    {
                        var n = Count(result);
                        if (n == 0) return "No inventory rows found.";
                        var lines = new List<string>();
                        if (result is System.Collections.IEnumerable e)
                        {
                            int i = 0;
                            foreach (var row in e)
                            {
                                var name = GetProp(row!, "product_name") ?? GetProp(row!, "ProductName") ?? "(unknown)";
                                var stock = GetProp(row!, "current_stock") ?? GetProp(row!, "CurrentStock") ?? "?";
                                lines.Add($"- {name} — {stock}");
                                if (++i >= 10) break;
                            }
                        }
                        return $"Inventory by product (preview): **{n}** items\n\n" + string.Join("\n", lines);
                    }

                case "low_stock":
                    {
                        var n = Count(result);
                        if (n == 0) return "No **low-stock** items right now.";
                        var lines = new List<string>();
                        if (result is System.Collections.IEnumerable e)
                        {
                            int i = 0;
                            foreach (var row in e)
                            {
                                var name = GetProp(row!, "product_name") ?? "(unknown)";
                                var stock = GetProp(row!, "current_stock") ?? "?";
                                var rp = GetProp(row!, "reorder_point") ?? "?";
                                lines.Add($"- {name} — {stock} (reorder ≤ {rp})");
                                if (++i >= 10) break;
                            }
                        }
                        return $"Low-stock items: **{n}**\n\n" + string.Join("\n", lines);
                    }

                case "out_of_stock":
                    {
                        var n = Count(result);
                        if (n == 0) return "No **out-of-stock** products 🎉";
                        var lines = new List<string>();
                        if (result is System.Collections.IEnumerable e)
                        {
                            int i = 0;
                            foreach (var row in e)
                            {
                                var name = GetProp(row!, "product_name") ?? "(unknown)";
                                lines.Add($"- {name}");
                                if (++i >= 10) break;
                            }
                        }
                        return $"Out-of-stock products: **{n}**\n\n" + string.Join("\n", lines);
                    }

                case "valuation":
                    {
                        // result = single row like { value_at_cost, value_at_price? }
                        var cost = GetProp(result!, "value_at_cost") ?? GetProp(result!, "ValueAtCost") ?? "0";
                        var price = GetProp(result!, "value_at_price") ?? GetProp(result!, "ValueAtPrice");
                        return price is null
                            ? $"Inventory valuation (cost): **{cost}**"
                            : $"Inventory valuation — cost: **{cost}**, price: **{price}**";
                    }

                case "snapshot":
                default:
                    {
                        // Fallback snapshot one-liner using whatever fields are present
                        var totalUnits = GetProp(result!, "total_units") ?? GetProp(result!, "TotalUnits");
                        var totalSkus = GetProp(result!, "total_skus") ?? GetProp(result!, "TotalSkus");
                        var low = GetProp(result!, "low_stock") ?? GetProp(result!, "LowStock");
                        var oos = GetProp(result!, "out_of_stock") ?? GetProp(result!, "OutOfStock");

                        var parts = new List<string>();
                        if (!string.IsNullOrEmpty(totalUnits)) parts.Add($"units: **{totalUnits}**");
                        if (!string.IsNullOrEmpty(totalSkus)) parts.Add($"SKUs: **{totalSkus}**");
                        if (!string.IsNullOrEmpty(low)) parts.Add($"low: **{low}**");
                        if (!string.IsNullOrEmpty(oos)) parts.Add($"OOS: **{oos}**");

                        return parts.Count == 0
                            ? "Inventory snapshot ready, but no KPI fields were returned."
                            : "Inventory snapshot — " + string.Join(", ", parts) + ".";
                    }
            }
        }

        // Unknown domain fallback
        return "Hi! I’m **BuiswAIz** — ask me about expense, sales, or inventory.";
    }

    private static string CleanNlqMarkdown(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;

        // remove: "(no prior...)" variants
        s = System.Text.RegularExpressions.Regex.Replace(
            s,
            @"\s*\((?:no\s+prior(?:-period)?\s+(?:data|revenue|expense|sales)[^)]*)\)\.?",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // remove: "(▲ 100% vs prior)" or "(▼ 100% vs prior)" (prior==0 artifacts)
        s = System.Text.RegularExpressions.Regex.Replace(
            s,
            @"\s*\((?:▲|▼)?\s*100(?:\.0+)?%\s+vs\s+prior\)\.?",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // trim leftover spaces
        return s.TrimEnd();
    }

    private async Task<object> BuildReportUiAsync(string userText, NlqResolvedPlan plan, CancellationToken ct)
    {
        var queries = _map.GetReportQueries(plan);

        var rows = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (qid, args) in queries)
            rows[qid] = await _catalog.RunAsync(qid, args, ct);

        JsonObject root = new()
        {
            ["report_title"] = plan.Domain.ToLowerInvariant() switch
            {
                "expense" => "Expense Report",
                "sales" => "Sales Report",
                "inventory" => "Inventory Snapshot",
                _ => "Report"
            },
            ["period"] = new JsonObject
            {
                ["start"] = plan.Start.ToString("yyyy-MM-dd"),
                ["end"] = plan.End.ToString("yyyy-MM-dd"),
                ["compare_to_prior"] = plan.CompareToPrior
            },
            ["kpis"] = new JsonArray(),
            ["cards"] = new JsonArray(),
            ["charts"] = new JsonArray(),
            ["narrative"] = new JsonArray(),
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "link",
                    ["label"] = "View full report",
                    ["href"] = $"/reports/{plan.Domain}?start={plan.Start:yyyy-MM-dd}&end={plan.End:yyyy-MM-dd}"
                }
            }
        };

        void AddNarrative(params string[] lines)
        {
            var narr = (JsonArray)root["narrative"]!;
            foreach (var s in lines) narr.Add(s);
        }

        switch (plan.Domain.ToLowerInvariant())
        {
            case "expense":
                {
                    var summary = rows["EXPENSE_SUMMARY"]!;
                    var total = GetDecimal(summary, "total");
                    var delta = GetDecimal(summary, "delta_pct");

                    ((JsonArray)root["kpis"]!).Add(new JsonObject
                    {
                        ["label"] = "Total Expense",
                        ["value"] = total,
                        ["delta_pct"] = delta
                    });

                    if (rows.TryGetValue("TOP_EXPENSE_CATEGORIES", out var tops) && tops is not null)
                    {
                        ((JsonArray)root["cards"]!).Add(new JsonObject
                        {
                            ["title"] = "Top Categories",
                            ["items"] = JsonSerializer.SerializeToNode(tops)
                        });
                    }

                    if (rows.TryGetValue("EXPENSE_BY_CATEGORY_WEEKLY", out var byWeek) && byWeek is not null)
                    {
                        ((JsonArray)root["charts"]!).Add(new JsonObject
                        {
                            ["type"] = "bar",
                            ["title"] = "Weekly Expense by Category",
                            ["data"] = JsonSerializer.SerializeToNode(byWeek)
                        });
                    }

                    AddNarrative(
                        $"Expenses from {plan.Start:yyyy-MM-dd} to {plan.End:yyyy-MM-dd} total {total:0.##}.",
                        plan.CompareToPrior && delta != 0 ? $"Change vs prior period: {delta:0.##}%." : "No prior-period comparison requested."
                    );
                    break;
                }
            case "sales":
                {
                    var summary = rows["SALES_SUMMARY"]!;
                    var revenue = GetDecimal(summary, "revenue");
                    var orders = GetInt(summary, "orders");
                    var units = GetInt(summary, "units");
                    var aov = GetDecimal(summary, "avg_order_value");
                    var delta = GetDecimal(summary, "delta_pct");

                    var kpis = (JsonArray)root["kpis"]!;
                    kpis.Add(new JsonObject { ["label"] = "Revenue", ["value"] = revenue, ["delta_pct"] = delta });
                    kpis.Add(new JsonObject { ["label"] = "Orders", ["value"] = orders });
                    kpis.Add(new JsonObject { ["label"] = "Units", ["value"] = units });
                    kpis.Add(new JsonObject { ["label"] = "AOV", ["value"] = aov });

                    if (rows.TryGetValue("TOP_PRODUCTS", out var top) && top is not null)
                    {
                        ((JsonArray)root["cards"]!).Add(new JsonObject
                        {
                            ["title"] = "Top Products",
                            ["items"] = JsonSerializer.SerializeToNode(top)
                        });
                    }

                    if (rows.TryGetValue("SALES_BY_DAY", out var ts) && ts is not null)
                    {
                        ((JsonArray)root["charts"]!).Add(new JsonObject
                        {
                            ["type"] = "line",
                            ["title"] = "Sales by Day",
                            ["data"] = JsonSerializer.SerializeToNode(ts)
                        });
                    }

                    AddNarrative(
                        $"Sales from {plan.Start:yyyy-MM-dd} to {plan.End:yyyy-MM-dd} drove {revenue:0.##} revenue with {orders} orders.",
                        plan.CompareToPrior && delta != 0 ? $"Revenue change vs prior period: {delta:0.##}%." : "No prior-period comparison requested."
                    );
                    break;
                }
            case "inventory":
                {
                    // Accept both legacy and new INV_* ids
                    object? invSnap = null;
                    if (!rows.TryGetValue("INV_SNAPSHOT", out invSnap))
                        rows.TryGetValue("INVENTORY_SNAPSHOT", out invSnap);

                    object? lowStock = null;
                    if (!rows.TryGetValue("INV_LOW_STOCK", out lowStock))
                        rows.TryGetValue("LOW_STOCK_ITEMS", out lowStock);

                    object? outOfStock = null;
                    if (!rows.TryGetValue("INV_OUT_OF_STOCK", out outOfStock))
                        rows.TryGetValue("OUT_OF_STOCK_ITEMS", out outOfStock);

                    object? byProduct = null;
                    if (!rows.TryGetValue("INV_BY_PRODUCT", out byProduct))
                        rows.TryGetValue("STOCK_BY_CATEGORY", out byProduct);

                    // KPIs — keep them simple & numeric for validator sanity
                    var kpis = (JsonArray)root["kpis"]!;
                    var lowCount = CountRows(lowStock);
                    var oosCount = CountRows(outOfStock);
                    kpis.Add(new JsonObject { ["label"] = "Low-stock", ["value"] = lowCount });
                    kpis.Add(new JsonObject { ["label"] = "OOS", ["value"] = oosCount });

                    // Cards
                    if (lowStock is not null)
                    {
                        ((JsonArray)root["cards"]!).Add(new JsonObject
                        {
                            ["title"] = "Low-stock Items",
                            ["items"] = JsonSerializer.SerializeToNode(lowStock)
                        });
                    }

                    if (outOfStock is not null)
                    {
                        ((JsonArray)root["cards"]!).Add(new JsonObject
                        {
                            ["title"] = "Out-of-stock Items",
                            ["items"] = JsonSerializer.SerializeToNode(outOfStock)
                        });
                    }

                    if (byProduct is not null)
                    {
                        ((JsonArray)root["cards"]!).Add(new JsonObject
                        {
                            ["title"] = "Inventory by Product",
                            ["items"] = JsonSerializer.SerializeToNode(byProduct)
                        });
                    }

                    // Narrative: at least two sentences for your validator
                    var snapText = $"Inventory snapshot for {plan.Start:yyyy-MM-dd}–{plan.End:yyyy-MM-dd}.";
                    var countsText = $"Low-stock: {lowCount}. Out-of-stock: {oosCount}.";
                    ((JsonArray)root["narrative"]!).Add(snapText);
                    ((JsonArray)root["narrative"]!).Add(countsText);

                    break;
                }

        }

        // --- materialize JSON once, validate, and return a live node ---
        var json = root.ToJsonString();

        // validate against your existing validator (uses JsonDocument)
        using (var uiDoc = JsonDocument.Parse(json))
        {
            _validator.ValidateUiSpec(uiDoc, rows);
        }

        // return a live JsonNode (no disposed references)
        return JsonNode.Parse(json)!.AsObject();
    }

    private static decimal GetDecimal(object row, string name)
    {
        var p = row.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        var v = p?.GetValue(row);
        return v is null ? 0 : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    private static int GetInt(object row, string name)
    {
        var p = row.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        var v = p?.GetValue(row);
        return v is null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    private static int CountRows(object? maybeSeq)
    {
        if (maybeSeq is System.Collections.IEnumerable seq)
        {
            int c = 0; foreach (var _ in seq) c++; return c;
        }
        return 0;
    }
}
