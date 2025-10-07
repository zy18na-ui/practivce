using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace dataAccess.Planning;

public sealed class PlannerService
{
    private readonly HttpClient _http;
    private readonly string _groqKey;
    private readonly string _registryJson;
    private readonly string _plannerSystem;
    private readonly GroqJsonClient _groq;

    public Task<System.Text.Json.JsonDocument> JsonPlanAsync(string system, string userPrompt) =>
        _groq.CompleteJsonAsync(system, userPrompt);

    public Task<System.Text.Json.JsonDocument> JsonRenderAsync(string system, string userPrompt, object rows) =>
        _groq.CompleteJsonAsync(system, userPrompt, rows);

    public PlannerService(IHttpClientFactory factory, IConfiguration cfg, PromptLoader prompts, BConfig appCfg, GroqJsonClient groq)
    {
        _http = factory.CreateClient();
        _groqKey = Environment.GetEnvironmentVariable("APP__GROQ__API_KEY") ?? "";
        _registryJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Planning", "SchemaRegistry.json"));
        _plannerSystem = BuildPlannerSystem(prompts, appCfg);

        _groq = groq;
    }

    private static string BuildPlannerSystem(PromptLoader prompts, BConfig cfg)
    {
        var identity = $"You are \"{cfg.identity.name}\". {cfg.identity.role}\n"; // from config.yaml
        var yaml = prompts.ReadText("planner.yaml"); // throws if missing

        // extract system: | block, or inline system:
        var m = System.Text.RegularExpressions.Regex.Match(
            yaml, @"(?ms)^\s*system\s*:\s*\|\s*\n(?<block>(?:[ \t]+.*\n?)+)");
        string body;
        if (m.Success)
        {
            var block = m.Groups["block"].Value.Replace("\r", "");
            var lines = block.Split('\n');
            var nonEmpty = lines.Where(l => l.Trim().Length > 0).ToList();
            var minIndent = nonEmpty.Count == 0 ? 0 : nonEmpty.Min(l => l.TakeWhile(char.IsWhiteSpace).Count());
            body = string.Join("\n", lines.Select(l => l.Length >= minIndent ? l[minIndent..] : l)).TrimEnd();
        }
        else
        {
            var m2 = System.Text.RegularExpressions.Regex.Match(yaml, @"(?m)^\s*system\s*:\s*(?<val>.+)$");
            body = m2.Success ? m2.Groups["val"].Value.Trim() : string.Empty;
        }

        return identity + body;
    }

    public async Task<QueryPlan> PlanAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new QueryPlan { Plan = new List<object>() };

        // If no key, skip straight to fallback
        if (string.IsNullOrWhiteSpace(_groqKey))
            return MakeHeuristicFallback(input);

        // Build request
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _groqKey);

        var systemPrompt = _plannerSystem;

        var payload = new
        {
            model = "gemma2-9b-it",
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = $"REGISTRY:\n{_registryJson}\n\nUSER:\n{input}" }
            },
            temperature = 0.1,
            response_format = new { type = "json_object" }
        };

        try
        {
            var resp = await _http.PostAsJsonAsync(
                "https://api.groq.com/openai/v1/chat/completions", payload, ct);

            if (!resp.IsSuccessStatusCode)
                return MakeHeuristicFallback(input);

            using var doc = await JsonDocument.ParseAsync(
                await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                return MakeHeuristicFallback(input);

            var plan = JsonSerializer.Deserialize<QueryPlan>(content);
            if (plan?.Plan is { Count: > 0 })
                return plan;

            return MakeHeuristicFallback(input);
        }
        catch
        {
            return MakeHeuristicFallback(input);
        }
    }

    private static int? TryExtractOrdinal(string lower)
    {
        // numeric ordinals: 1st, 2nd, 3rd, 4th, ...
        var m = System.Text.RegularExpressions.Regex.Match(lower, @"\b(\d+)(st|nd|rd|th)\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0) return n;

        // word ordinals up to 20 (extend if needed)
        var ord = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["first"] = 1,
            ["second"] = 2,
            ["third"] = 3,
            ["fourth"] = 4,
            ["fifth"] = 5,
            ["sixth"] = 6,
            ["seventh"] = 7,
            ["eighth"] = 8,
            ["ninth"] = 9,
            ["tenth"] = 10,
            ["eleventh"] = 11,
            ["twelfth"] = 12,
            ["thirteenth"] = 13,
            ["fourteenth"] = 14,
            ["fifteenth"] = 15,
            ["sixteenth"] = 16,
            ["seventeenth"] = 17,
            ["eighteenth"] = 18,
            ["nineteenth"] = 19,
            ["twentieth"] = 20
        };
        foreach (var kv in ord) if (lower.Contains(kv.Key)) return kv.Value;
        return null;
    }

    private static string InferSortDirForOrdinal(string lower, bool wantExpensive, bool wantCheapest)
    {
        // Prefer explicit phrasing in the text
        if (lower.Contains("cheapest") || lower.Contains("lowest")) return "asc";
        if (lower.Contains("most expensive") || lower.Contains("expensive") || lower.Contains("highest")) return "desc";
        // Fall back to flags computed by your code
        if (wantCheapest) return "asc";
        if (wantExpensive) return "desc";
        // Default to "desc" if unspecified
        return "desc";
    }

    private static readonly HashSet<string> ProductGenerics =
       new HashSet<string>(StringComparer.OrdinalIgnoreCase)
       {
        "onesie","onesies","shirt","shirts","tee","tshirt","t-shirt","pants","shorts","pajamas","pajama",
        "romper","bodysuit","hoodie","jacket","dress","skirt","socks","cap","hat","shoes","footwear",
        "clothes","clothing","apparel","garment","wear"
       };

    private QueryPlan MakeHeuristicFallback(string userInput)
    {
        var text = userInput ?? string.Empty;
        var lower = text.ToLowerInvariant();

        // Clean phrase for ANN (may return empty when input is generic-only)
        var searchText = ExtractSearchText(text);

        // Derive keywords from the cleaned phrase
        var productKeywords = ExtractKeywords(searchText.Length > 0 ? searchText.ToLowerInvariant() : text.ToLowerInvariant());
        var supplierKeywords = productKeywords;

        var wantSupplier = lower.Contains("supplier");
        var wantExpensive = lower.Contains("most expensive") || lower.Contains("expensive") || lower.Contains("highest");
        var wantCheapest = lower.Contains("cheapest") || lower.Contains("lowest") || lower.Contains("low price");
        var wantAll = lower.Contains("show all") || lower.Contains("list all") || lower.Contains("all products");

        int? topN = TryExtractTopN(lower);
        var limit = wantAll ? 50 : (topN ?? (wantSupplier ? 5 : 20));
        limit = Math.Clamp(limit, 1, 200);

        // price filters
        var where = new List<object>();
        var between = TryExtractBetween(lower);
        if (between is not null)
        {
            where.Add(new { field = "price", op = "gte", value = between.Value.a });
            where.Add(new { field = "price", op = "lte", value = between.Value.b });
        }
        else
        {
            var one = TryExtractPriceBound(lower);
            if (one is not null)
                where.Add(new { field = "price", op = one.Value.op, value = one.Value.value });
        }

        // ---------- Generic-only guard (BEFORE "show all products") ----------
        if (productKeywords.Count > 0 && productKeywords.All(k => ProductGenerics.Contains(k)))
        {
            // IMPORTANT: no vector_search; no ids_in; just SELECT with keywords
            return new QueryPlan
            {
                Plan = new List<object>
            {
                new {
                    op = "select",
                    entity = "productcategory",
                    keywords = productKeywords,
                    sort = new[] { new { field = "price", dir = "asc" } },
                    limit
                }
            }
            };
        }

        // --- "show all products" (only if truly no useful keywords and no supplier/price filters) ---
        if (wantAll && where.Count == 0 && !wantSupplier && productKeywords.Count == 0)
        {
            return new QueryPlan
            {
                Plan = new List<object>
            {
                new {
                    op = "select",
                    entity = "productcategory",
                    sort = new[] { new { field = "price", dir = "asc" } },
                    limit
                }
            }
            };
        }

        // --- Cheapest/Most expensive ---
        if (wantCheapest || wantExpensive)
        {
            var dir = wantExpensive ? "desc" : "asc";
            return new QueryPlan
            {
                Plan = new List<object>
            {
                new {
                    op = "select",
                    entity = "productcategory",
                    keywords = productKeywords.Count > 0 ? productKeywords : null,
                    sort = new[] { new { field = "price", dir } },
                    limit
                }
            }
            };
        }

        // --- Supplier listing ---
        if (wantSupplier)
        {
            return new QueryPlan
            {
                Plan = new List<object>
            {
                new {
                    op = "select",
                    entity = "supplier",
                    keywords = supplierKeywords.Count > 0 ? supplierKeywords : null,
                    limit
                }
            }
            };
        }

        // --- Default: ANN (only when we have a meaningful phrase) -> select ---
        var steps = new List<object>();

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            steps.Add(new
            {
                op = "vector_search",
                entity = "product",
                text = searchText,
                topk = 100,
                @return = "ids"
            });
        }

        var select = new Dictionary<string, object?>
        {
            ["op"] = "select",
            ["entity"] = "productcategory",
            ["sort"] = new[] { new { field = "price", dir = "asc" } },
            ["limit"] = limit
        };

        if (!string.IsNullOrWhiteSpace(searchText))
            select["ids_in"] = "ids";

        if (productKeywords.Count > 0)
            select["keywords"] = productKeywords;

        if (where.Count > 0)
            select["where"] = where;

        steps.Add(select);
        return new QueryPlan { Plan = steps };
    }

    private static string ExtractSearchText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // prefer quoted phrase if present
        var q = System.Text.RegularExpressions.Regex.Match(raw, "\"([^\"]+)\"");
        if (q.Success) return CleanTokens(q.Groups[1].Value);

        // otherwise normalize everything
        var cleaned = CleanTokens(raw);

        // guard: if cleaned is a single generic term (e.g., "onesie"), return empty
        // so the planner can skip ANN and just use keywords in SELECT.
        if (ProductGenerics.Contains(cleaned.Trim()))
            return string.Empty;

        return cleaned;
    }

    private static string CleanTokens(string s)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cheapest","most","expensive","lowest","highest","price","cost","list","show","find",
            "me","the","a","an","of","for","under","over","top","items","item","products","product",
            "supplier","suppliers","with","and","or","to","please"
        };

        // normalize and split
        var cleaned = Regex.Replace(s, @"[^a-z0-9\s-]", " ").Trim();
        var tokens = cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        // drop pure-number tokens anywhere
        tokens = tokens.Where(t => !Regex.IsMatch(t, @"^\d+$")).ToList();

        // remove stopwords
        tokens = tokens.Where(t => !stop.Contains(t)).ToList();

        if (tokens.Count == 0) return cleaned;

        // keep last 2..5 tokens (product words often at the tail)
        var take = Math.Min(Math.Max(tokens.Count, 2), 5);
        var slice = tokens.Skip(Math.Max(0, tokens.Count - take)).Take(take);

        var phrase = string.Join(" ", slice).Trim();

        // final guard: if phrase starts with a number, strip it
        phrase = Regex.Replace(phrase, @"^\d+\s*", "");

        return string.IsNullOrWhiteSpace(phrase) ? cleaned : phrase;
    }

    // NOTE: keep your existing TryExtractTopN if you prefer; this one is the same idea.
    private static int? TryExtractTopN(string lower)
    {
        // "top 3", "3 cheapest", "3 most expensive"
        var m = Regex.Match(lower, @"\btop\s*(\d+)|\b(\d+)\s*(cheapest|expensive|items?)");
        if (m.Success)
        {
            for (int i = 1; i < m.Groups.Count; i++)
                if (int.TryParse(m.Groups[i].Value, out var n) && n > 0 && n <= 50)
                    return n;
        }

        var words = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10
        };
        foreach (var kv in words)
            if (lower.Contains($"top {kv.Key}") || Regex.IsMatch(lower, $@"\b{kv.Key}\b"))
                return kv.Value;

        return null;
    }

    private static (string op, decimal value)? TryExtractPriceBound(string lower)
    {
        // ignore numeric ordinals
        lower = Regex.Replace(lower, @"\b(\d+)(st|nd|rd|th)\b", " ");

        // Require either an explicit comparator OR a price/cost word
        // Examples matched: "price > 100", ">= 99", "cost less than 50", "over 200"
        var m = Regex.Match(
            lower,
            @"\b(?:price|cost)?\s*(>=|<=|>|<|=)\s*(\d+(\.\d+)?)\b|\b(?:price|cost)\s*(?:above|over|greater than|less than)\s*(\d+(\.\d+)?)\b");

        if (!m.Success) return null;

        if (m.Groups[1].Success && decimal.TryParse(m.Groups[2].Value, out var num1))
        {
            var sym = m.Groups[1].Value;
            return sym switch
            {
                ">" => ("gt", num1),
                ">=" => ("gte", num1),
                "<" => ("lt", num1),
                "<=" => ("lte", num1),
                "=" => ("eq", num1),
                _ => (null, 0m)  // unreachable
            };
        }

        if (decimal.TryParse(m.Groups[4].Value, out var num2))
        {
            // worded comparator: default "above/over/greater than" => gt; "less than" => lt
            var wordPart = m.Value;
            if (wordPart.Contains("less than")) return ("lt", num2);
            return ("gt", num2);
        }

        return null;
    }

    private static (decimal a, decimal b)? TryExtractBetween(string lower)
    {
        var m = Regex.Match(lower, @"between\s+(\d+(\.\d+)?)\s+(and|-)\s+(\d+(\.\d+)?)");
        if (!m.Success) return null;
        var a = decimal.Parse(m.Groups[1].Value);
        var b = decimal.Parse(m.Groups[4].Value);
        if (a > b) (a, b) = (b, a);
        return (a, b);
    }

    private static List<string> ExtractKeywords(string lower)
    {
        if (string.IsNullOrWhiteSpace(lower)) return new List<string>();

        // normalize via your cleaner; this trims junk like "can", "you", "all"
        var phrase = CleanTokens(lower);

        // strong boilerplate/aux verbs/pronouns/etc. (make sure "can","you","all" are here)
        var boiler = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "find","show","list","give","me","all","the","a","an","of","for","under","over","between",
            "and","or","to","please","cheap","cheaper","cheapest","expensive","priciest","most","least",
            "lowest","highest","price","prices","cost","supplier","suppliers","with","in","on","at","by",
            "item","items","product","products","can","you","your","my"
        };

        var norm = System.Text.RegularExpressions.Regex.Replace(phrase.ToLowerInvariant(), @"[^a-z0-9\- ]", " ");
        var toks = norm.Split(new[] { ' ', ',', '.', ':', ';', '-' }, StringSplitOptions.RemoveEmptyEntries)
                       .Where(t => !System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+$")) // drop pure numbers
                       .Where(t => !boiler.Contains(t))
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Take(8)
                       .ToList();

        if (toks.Count == 0) return new List<string>();

        // generic handling:
        // - if ALL tokens are generics (e.g., "onesie"), KEEP them (we want keywords:["onesie"])
        // - else (mixed), DROP generics (e.g., "dinosaur onesie" -> ["dinosaur"])
        var allGeneric = toks.All(t => ProductGenerics.Contains(t));
        if (allGeneric) return toks;

        return toks.Where(t => !ProductGenerics.Contains(t)).ToList();
    }


    public Task<JsonDocument> JsonPlanAsync(string system, string user, CancellationToken ct)
        => _groq.CompleteJsonAsync(system, user, data: null, ct);

    // New overload: system+user+data+ct
    public Task<JsonDocument> JsonPlanAsync(string system, string user, object? data, CancellationToken ct)
        => _groq.CompleteJsonAsync(system, user, data, ct);

}