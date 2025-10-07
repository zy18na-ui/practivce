using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ISqlCatalog = dataAccess.Services.ISqlCatalog;
using static dataAccess.Reports.SectionBundles;

namespace dataAccess.Reports
{
    /// <summary>
    /// Deterministic Phase-1 (bundles) → SqlCatalog → YAML Phase-2 (LLM).
    /// Helpers:
    /// - DateRangeResolver (PH time + compare detection)
    /// - YamlPreprocessor (guards, compare rules)
    /// - IGroqJsonClient + GroqAdapter (model adapter)
    /// - SectionBundles (fixed QID bundles)
    /// 
    /// API must provide a loader: Func<string, CancellationToken, Task<string>> (loads Phase-2 system text).
    /// </summary>
    public sealed class YamlReportRunner
    {
        private readonly YamlPreprocessor _pre;
        private readonly ISqlCatalog _sql;
        private readonly IGroqJsonClient _groq;
        private readonly Func<string, CancellationToken, Task<string>> _loadPhase2;
        private readonly IReportRunStore _reportStore;

        public YamlReportRunner(
            YamlPreprocessor pre,
            ISqlCatalog sql,
            IGroqJsonClient groq,
            IReportRunStore reportStore,
            Func<string, CancellationToken, Task<string>> loadPhase2System)
        {
            _pre = pre;
            _sql = sql;
            _groq = groq;
            _loadPhase2 = loadPhase2System;
            _reportStore = reportStore;
        }

        public async Task<object> RunAsync(string domain, string userText, CancellationToken ct)
        {
            // 1) Preprocess (time window + guardrails)
            var prep = _pre.Prepare(domain, userText);
            if (!prep.Allowed) throw new InvalidOperationException(prep.Message ?? "Not allowed.");
            var h = prep.Data;
            if (string.IsNullOrEmpty(h.Start) || string.IsNullOrEmpty(h.End))
                throw new InvalidOperationException("Start/End missing.");

            // 2) Build deterministic requests per domain (SectionBundles)
            var requests = BuildRequests(domain, h);

            // 3) Execute SqlCatalog per query → rows bag keyed by query_id (+#2 for dupes)
            var rows = await RunBatchAsync(domain, requests, ct);

            // 3.1) Formatting hints (₱ PHP). Keep feature flags simple; budgets removed.
            var fmt = new
            {
                currency = "PHP",
                symbol = "₱",
                locale = "en-PH",
                money_decimals = 2,
                pct_decimals = 2,
                use_thousands = true
            };
            var feature_flags = new
            {
                show_budget = false // hard-off; no budget queries
            };

            // 4) Build sections payload (always object[] to avoid CS0173)
            var sectionsPayload = requests
                .GroupBy(q => SectionId(NormalizeDomain(domain), q.QueryId))
                .Select(g => new { id = g.Key, queries = g.Select(x => x.QueryId).ToArray() })
                .Cast<object>()
                .ToArray();

            var phase2Input = new
            {
                period = new { start = h.Start, end = h.End, label = h.Label },
                compare_to_prior = h.CompareToPrior,
                sections = sectionsPayload,
                rows,
                fmt,
                feature_flags
            };

            // 5) Load Phase-2 system text from YAML and render
            var specFile = NormalizeDomain(domain) switch
            {
                "expenses" => "reports.expense.yaml",
                "sales" => "reports.sales.yaml",
                "inventory" => "reports.inventory.yaml",
                _ => throw new InvalidOperationException($"Unknown domain: {domain}")
            };
            var system = await _loadPhase2(specFile, ct);
            var doc = await _groq.CompleteJsonAsync(system, new { input = phase2Input }, ct);

            // 6) Save run
            var metaNode = new JsonObject
            {
                ["filters"] = null,
                ["user_id"] = null,
                ["router_mode"] = "report"
            };
            using var metaDoc = JsonDocument.Parse(metaNode.ToJsonString());

            var normalizedDomain = NormalizeDomain(domain);
            var record = new dataAccess.Reports.ReportRecord(
                Domain: normalizedDomain,                // e.g., "expenses" (plural) for consistency
                Scope: h.Scope ?? "overall",
                ReportType: "standard",
                ProductId: h.ProductId,                         // null when overall
                PeriodStart: h.Start!,
                PeriodEnd: h.End!,
                PeriodLabel: h.Label ?? $"{h.Start}–{h.End}",
                CompareToPrior: h.CompareToPrior,
                TopK: (short?)(h.TopK ?? 10),
                YamlName: specFile,
                YamlVersion: null,
                ModelName: "groq/gemma2-9b-it",                 // keep in sync with YAML
                UiSpec: doc,
                Meta: metaDoc
            );

            await _reportStore.SaveAsync(record, ct);

            // 7) Return strict JSON UI
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText())!;
        }

        // ---------- Prompt utilities (kept) ----------
        public async Task<object> RunPromptAsync(string id, object? args, CancellationToken ct = default)
        {
            string ToCandidate(string s)
            {
                s = s.Trim();
                if (s.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) return s;
                if (s.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase) ||
                    s.StartsWith("prompts.", StringComparison.OrdinalIgnoreCase))
                    return s + ".yaml";
                return s + ".yaml";
            }

            var tried = new List<string>();
            foreach (var cand in new[] { ToCandidate(id), "chitchat.yaml", "prompts/chitchat.yaml", "prompts.chitchat.yaml" })
            {
                try
                {
                    var system = await _loadPhase2(cand, ct);
                    var payload = new { input = args ?? new { } };
                    var doc = await _groq.CompleteJsonAsync(system, payload, ct);
                    return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText())!;
                }
                catch
                {
                    tried.Add(cand);
                }
            }

            throw new InvalidOperationException("Prompt spec not found or failed to render. Tried: " + string.Join(", ", tried));
        }

        public async Task<object> RunPromptFileAsync(string fullPath, object? args, CancellationToken ct = default)
        {
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Prompt YAML not found", fullPath);

            var yaml = await File.ReadAllTextAsync(fullPath, ct);
            var payload = new { input = args ?? new { } };
            var doc = await _groq.CompleteJsonAsync(yaml, payload, ct);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText())!;
        }

        // ---------- Request bundling ----------
        private static IReadOnlyList<SectionBundles.QuerySpec> BuildRequests(string domain, YamlPreprocessor.Hints h)
        {
            var list = new List<SectionBundles.QuerySpec>();
            var dom = NormalizeDomain(domain);

            switch (dom)
            {
                case "sales":
                    {
                        var topK = h.TopK is > 0 ? h.TopK!.Value : 10;
                        var scope = (h.Scope ?? "overall").ToLowerInvariant();

                        if (scope == "item")
                        {
                            if (string.IsNullOrWhiteSpace(h.ProductId))
                                throw new InvalidOperationException("ProductId is required for item-scoped sales report.");

                            list.AddRange(Sales.ItemPerformanceQueries(h.Start!, h.End!, h.ProductId!, h.CompareToPrior, h.PrevStart, h.PrevEnd));
                            list.AddRange(Sales.ItemVariantsQueries(h.Start!, h.End!, h.ProductId!, topK));
                            list.AddRange(Sales.ItemTrendQueries(h.Start!, h.End!, h.ProductId!));
                        }
                        else // overall
                        {
                            list.AddRange(Sales.PerformanceQueries(h.Start!, h.End!, h.CompareToPrior, h.PrevStart, h.PrevEnd));
                            list.AddRange(Sales.BestSellingQueries(h.Start!, h.End!, topK));
                            list.AddRange(Sales.TrendQueries(h.Start!, h.End!));
                        }
                        break;
                    }

                case "expenses":
                    {
                        // IMPORTANT: disable budgets by passing ym = null so bundles won't add budget QIDs.
                        string? ym = null;
                        list.AddRange(Expense.OverviewQueries(h.Start!, h.End!, h.CompareToPrior, h.PrevStart, h.PrevEnd, ym));
                        list.AddRange(Expense.TopCategoryQueries(h.Start!, h.End!, 10));
                        list.AddRange(Expense.AnomalyQueries(h.Start!, h.End!));
                        break;
                    }

                case "inventory":
                    {
                        var asOf = h.End!;
                        list.AddRange(Inventory.SnapshotQueries(asOf));
                        list.AddRange(Inventory.SlowMoverQueries(h.Start!, h.End!));
                        list.AddRange(Inventory.DemandTrendQueries(h.Start!, h.End!, 10));
                        break;
                    }

                default:
                    throw new InvalidOperationException($"Unknown domain: {domain}");
            }

            return list;
        }

        // ---------- Batch executor with whitelist & defensive skip ----------
        private async Task<Dictionary<string, object?>> RunBatchAsync(
            string domain,
            IEnumerable<SectionBundles.QuerySpec> requests,
            CancellationToken ct)
        {
            var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var dup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Whitelists per normalized domain
            var dom = NormalizeDomain(domain);
            var white = dom switch
            {
                "sales" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "SALES_SUMMARY","TOP_PRODUCTS","SALES_BY_DAY",
                    "ITEM_SUMMARY","ITEM_VARIANTS_TOP","ITEM_TRENDS_DAILY"
                },
                "expenses" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "EXPENSE_SUMMARY","TOP_EXPENSE_CATEGORIES","EXPENSE_BY_CATEGORY_WEEKLY"
                },
                "inventory" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "INV_AVAILABLE_PRODUCTS","INV_LOW_STOCK","INV_OUT_OF_STOCK",
                    "INV_BY_PRODUCT","SALES_BY_PRODUCT_DAY"
                },
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var r in requests)
            {
                var qid = r.QueryId;
                if (!white.Contains(qid))
                {
                    // Skip unknown/disabled query IDs (e.g., BUDGET_UTILIZATION)
                    Console.WriteLine($"[report-runner] Skipping unknown query_id: {qid}");
                    continue;
                }

                var key = qid;
                if (bag.ContainsKey(key))
                {
                    dup[key] = dup.TryGetValue(key, out var n) ? n + 1 : 2;
                    key = $"{key}#{dup[key]}";
                }

                try
                {
                    var val = await _sql.RunAsync(qid, r.Args, ct); // real SqlCatalog
                    bag[key] = val;
                }
                catch (ArgumentOutOfRangeException ex) when (string.Equals(ex.ParamName, "queryId", StringComparison.OrdinalIgnoreCase))
                {
                    // Defensive: if catalog still doesn't know this QID, skip it gracefully
                    Console.WriteLine($"[report-runner] Catalog rejected query_id {qid}: {ex.Message}");
                    bag[key] = Array.Empty<object>();
                }
            }

            return bag;
        }

        // ---------- Section mapper (IDs must match UI/assistant renderers) ----------
        private static string SectionId(string domain, string qid) => domain switch
        {
            "sales" => qid switch
            {
                // overall
                "SALES_SUMMARY" => "sales_performance",
                "TOP_PRODUCTS" => "best_selling",
                "SALES_BY_DAY" => "sale_trends",

                // item-specific
                "ITEM_SUMMARY" => "sales_performance",
                "ITEM_VARIANTS_TOP" => "best_selling",
                "ITEM_TRENDS_DAILY" => "sale_trends",

                _ => "sales_misc"
            },

            // EXPENSES (plural)
            "expenses" => qid switch
            {
                "EXPENSE_SUMMARY" => "expense_overview",
                "TOP_EXPENSE_CATEGORIES" => "expense_top_categories",
                "EXPENSE_BY_CATEGORY_WEEKLY" => "expense_spikes",
                _ => "expense_misc"
            },

            "inventory" => qid switch
            {
                "INV_AVAILABLE_PRODUCTS" => "availability_snapshot",
                "INV_LOW_STOCK" => "availability_snapshot",
                "INV_OUT_OF_STOCK" => "availability_snapshot",
                "INV_BY_PRODUCT" => "slow_movers",
                "SALES_BY_PRODUCT_DAY" => "demand_trend",
                _ => "inventory_misc"
            },

            _ => "misc"
        };

        private static string NormalizeDomain(string domain)
        {
            if (string.Equals(domain, "expense", StringComparison.OrdinalIgnoreCase))
                return "expenses";
            return domain?.Trim().ToLowerInvariant() ?? "";
        }
    }
}
