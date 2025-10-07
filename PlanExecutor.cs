using System.Text.Json;
using dataAccess.Services;
using System.Linq;
using Shared.DTOs.Catalog;

namespace dataAccess.Planning;

public sealed class ProductWithPriceDto
{
    public int ProductId { get; set; }
    public string? ProductName { get; set; }
    public string? Description { get; set; }  // maps from ProductDto.ProductDescription
    public string? ImageUrl { get; set; }
    public int? SupplierId { get; set; }
    public decimal? Price { get; set; }
    public decimal? Cost { get; set; }
    public int? ProductCategoryId { get; set; }
}

public sealed class PlanExecutor
{
    private readonly SqlQueryService _sql;
    private readonly VectorSearchService _vec;

    public PlanExecutor(SqlQueryService sql, VectorSearchService vec)
    {
        _sql = sql;
        _vec = vec;
    }

    // --- helpers -------------------------------------------------------------
    private static string NormalizeToken(string s)
    {
        s ??= "";
        s = s.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9\- ]", "");
        s = s.Replace('-', ' '); // hyphen-insensitive
        return s.Trim();
    }

    private static IEnumerable<string> Tokenize(string s)
    {
        s = NormalizeToken(s);
        return s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1);
    }

    // Small, fast Levenshtein
    private static int Levenshtein(string a, string b)
    {
        var n = a.Length; var m = b.Length;
        if (n == 0) return m; if (m == 0) return n;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private static bool FuzzyTokenEquals(string a, string b)
    {
        a = NormalizeToken(a);
        b = NormalizeToken(b);
        if (a == b) return true;

        // short tokens tolerate distance 1; longer 2
        var maxEd = (a.Length <= 5 || b.Length <= 5) ? 1 : 2;
        if (Levenshtein(a, b) <= maxEd) return true;

        // prefix containment with min overlap 4 (covers dino~dinosaur)
        var min = Math.Min(a.Length, b.Length);
        if (min >= 4 && (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal)))
            return true;

        return false;
    }

    private static IEnumerable<ProductCategoryDto> ApplyWhere(
        IEnumerable<ProductCategoryDto> src,
        JsonElement whereArr)
    {
        var list = src;
        if (whereArr.ValueKind != JsonValueKind.Array) return list;

        foreach (var w in whereArr.EnumerateArray())
        {
            if (w.ValueKind != JsonValueKind.Object) continue;
            var field = w.TryGetProperty("field", out var f) ? f.GetString()?.ToLowerInvariant() : null;
            var op = w.TryGetProperty("op", out var o) ? o.GetString()?.ToLowerInvariant() : null;
            var valEl = w.TryGetProperty("value", out var v) ? v : default;

            if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(op)) continue;

            bool TryGetDecimal(out decimal d)
            {
                d = 0m;
                if (valEl.ValueKind == JsonValueKind.Number)
                {
                    try { d = valEl.GetDecimal(); return true; } catch { return false; }
                }
                if (valEl.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(valEl.GetString(), out d))
                    return true;
                return false;
            }

            switch (field)
            {
                case "price":
                    if (TryGetDecimal(out var p))
                        list = op switch
                        {
                            "gt" => list.Where(x => x.Price > p),
                            "gte" => list.Where(x => x.Price >= p),
                            "lt" => list.Where(x => x.Price < p),
                            "lte" => list.Where(x => x.Price <= p),
                            "eq" => list.Where(x => x.Price == p),
                            _ => list
                        };
                    break;

                case "cost":
                    if (TryGetDecimal(out var c))
                        list = op switch
                        {
                            "gt" => list.Where(x => x.Cost > c),
                            "gte" => list.Where(x => x.Cost >= c),
                            "lt" => list.Where(x => x.Cost < c),
                            "lte" => list.Where(x => x.Cost <= c),
                            "eq" => list.Where(x => x.Cost == c),
                            _ => list
                        };
                    break;

                case "productid":
                    if (valEl.ValueKind == JsonValueKind.Number &&
                        valEl.TryGetInt32(out var pid))
                        list = op switch
                        {
                            "eq" => list.Where(x => x.ProductId == pid),
                            _ => list
                        };
                    break;

                case "productcategoryid":
                    if (valEl.ValueKind == JsonValueKind.Number &&
                        valEl.TryGetInt32(out var pcid))
                        list = op switch
                        {
                            "eq" => list.Where(x => x.ProductCategoryId == pcid),
                            _ => list
                        };
                    break;
            }
        }

        return list;
    }

    // --- executor ------------------------------------------------------------

    public async Task<object> ExecuteAsync(QueryPlan plan, CancellationToken ct)
    {
        var vars = new Dictionary<string, object?>();
        Console.WriteLine($"[plan] {System.Text.Json.JsonSerializer.Serialize(plan)}");

        if (plan?.Plan is null || plan.Plan.Count == 0)
            return Array.Empty<object>();

        foreach (var stepObj in plan.Plan)
        {
            if (stepObj is not JsonElement je || je.ValueKind != JsonValueKind.Object)
                continue;

            string? opType = null;
            if (je.TryGetProperty("Op", out var opProp)) opType = opProp.GetString();
            if (opType is null && je.TryGetProperty("op", out var opProp2)) opType = opProp2.GetString();

            switch (opType)
            {
                case "vector_search":
                    {
                        var entity = je.GetProperty("entity").GetString() ?? "";
                        var text = je.GetProperty("text").GetString() ?? "";
                        var topk = je.TryGetProperty("topk", out var kEl) && kEl.ValueKind == JsonValueKind.Number
                                       ? kEl.GetInt32()
                                       : 10;
                        var retKey = je.TryGetProperty("return", out var rEl) ? (rEl.GetString() ?? "ids") : "ids";

                        if (entity.Equals("product", StringComparison.OrdinalIgnoreCase))
                        {
                            // 🚫 Skip ANN if the search text is empty/generic
                            if (string.IsNullOrWhiteSpace(text) ||
                                text.Equals("product", StringComparison.OrdinalIgnoreCase) ||
                                text.Equals("products", StringComparison.OrdinalIgnoreCase) ||
                                text.Equals("item", StringComparison.OrdinalIgnoreCase) ||
                                text.Equals("items", StringComparison.OrdinalIgnoreCase))
                            {
                                vars[retKey] = new List<int>();
                                break;
                            }

                            var ids = await _vec.SearchProductIdsAsync(text, topk, null, ct);
                            Console.WriteLine($"[PlanExecutor] vector ids for '{text}': {string.Join(",", ids)}");
                            vars[retKey] = ids;
                        }
                        else
                        {
                            vars[retKey] = new List<int>();
                        }
                        break;
                    }

                case "select":
                    {
                        var entity = je.GetProperty("entity").GetString() ?? "";
                        var idsInVar = je.TryGetProperty("IdsIn", out var idsEl)
                            ? idsEl.GetString()
                            : (je.TryGetProperty("ids_in", out var ids2) ? ids2.GetString() : null);
                        var limit = je.TryGetProperty("limit", out var lEl) && lEl.ValueKind == JsonValueKind.Number
                            ? lEl.GetInt32()
                            : (int?)null;
                        var offset = je.TryGetProperty("offset", out var offEl) && offEl.ValueKind == JsonValueKind.Number
                            ? Math.Max(0, offEl.GetInt32())
                            : 0;

                        // normalize ids_in (usually product IDs from the vector step)
                        var productIds = new List<int>();
                        if (!string.IsNullOrWhiteSpace(idsInVar)
                            && vars.TryGetValue(idsInVar!, out var v)
                            && v is List<int> list)
                        {
                            productIds = list;
                        }

                        // ---- productcategory path ----
                        if (entity.Equals("productcategory", StringComparison.OrdinalIgnoreCase))
                        {
                            var rows = await _sql.GetProductCategoriesByProductIdsAsync(productIds, ct);

                            // --- keywords ---
                            List<string> kws = new();
                            if (je.TryGetProperty("keywords", out var kwEl) && kwEl.ValueKind == JsonValueKind.Array)
                            {
                                kws = kwEl.EnumerateArray()
                                          .Where(x => x.ValueKind == JsonValueKind.String)
                                          .Select(x => (x.GetString() ?? "").Trim())
                                          .Where(s => !string.IsNullOrWhiteSpace(s))
                                          .Select(NormalizeToken)
                                          .Distinct()
                                          .ToList();
                            }

                            // Apply price/cost/etc. filters BEFORE any ordinal logic
                            if (je.TryGetProperty("where", out var whereArr))
                            {
                                rows = ApplyWhere(rows, whereArr).ToList();
                            }

                            // parse sort keys
                            var sortKeys = new List<(string field, bool desc)>();
                            if (je.TryGetProperty("sort", out var sortArr) && sortArr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var s in sortArr.EnumerateArray())
                                {
                                    if (s.ValueKind != JsonValueKind.Object) continue;
                                    var field = s.TryGetProperty("field", out var fEl) ? fEl.GetString() ?? "" : "";
                                    var dir = s.TryGetProperty("dir", out var dEl) ? dEl.GetString() ?? "asc" : "asc";
                                    if (!string.IsNullOrWhiteSpace(field))
                                        sortKeys.Add((field.ToLowerInvariant(),
                                                      string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)));
                                }
                            }

                            // --- ORDINAL MODE? ---
                            var ordinalMode = je.TryGetProperty("offset", out var _);
                            if (rows.Count == 0)
                            {
                                vars["last"] = Array.Empty<object>();
                                break;
                            }

                            if (ordinalMode)
                            {
                                // keep your existing ordinal logic
                                IEnumerable<ProductCategoryDto> orderedAll = rows;
                                if (sortKeys.Count > 0)
                                {
                                    var first = sortKeys[0];
                                    IOrderedEnumerable<ProductCategoryDto> ord = first.field switch
                                    {
                                        "price" => first.desc ? orderedAll.OrderByDescending(x => x.Price) : orderedAll.OrderBy(x => x.Price),
                                        "cost" => first.desc ? orderedAll.OrderByDescending(x => x.Cost) : orderedAll.OrderBy(x => x.Cost),
                                        "updatedstock" => first.desc ? orderedAll.OrderByDescending(x => x.UpdatedStock) : orderedAll.OrderBy(x => x.UpdatedStock),
                                        "productcategoryid" => first.desc ? orderedAll.OrderByDescending(x => x.ProductCategoryId) : orderedAll.OrderBy(x => x.ProductCategoryId),
                                        _ => first.desc ? orderedAll.OrderByDescending(x => x.Price) : orderedAll.OrderBy(x => x.Price),
                                    };
                                    for (int i = 1; i < sortKeys.Count; i++)
                                    {
                                        var k = sortKeys[i];
                                        ord = k.field switch
                                        {
                                            "price" => k.desc ? ord.ThenByDescending(x => x.Price) : ord.ThenBy(x => x.Price),
                                            "cost" => k.desc ? ord.ThenByDescending(x => x.Cost) : ord.ThenBy(x => x.Cost),
                                            "updatedstock" => k.desc ? ord.ThenByDescending(x => x.UpdatedStock) : ord.ThenBy(x => x.UpdatedStock),
                                            "productcategoryid" => k.desc ? ord.ThenByDescending(x => x.ProductCategoryId) : ord.ThenBy(x => x.ProductCategoryId),
                                            _ => k.desc ? ord.ThenByDescending(x => x.Price) : ord.ThenBy(x => x.Price),
                                        };
                                    }
                                    orderedAll = ord;
                                }
                                else
                                {
                                    orderedAll = rows.OrderBy(x => x.Price);
                                }

                                var offsetVal = offset;
                                var limitVal = limit ?? 1;
                                var sliced = orderedAll.Skip(offsetVal).Take(limitVal).ToList();

                                if (sliced.Count == 0)
                                {
                                    vars["last"] = Array.Empty<object>();
                                    break;
                                }

                                var pidsAll = sliced.Select(pc => pc.ProductId).Distinct().ToList();
                                var productsAll = await _sql.GetProductsByIdsAsync(pidsAll, ct);

                                var joinedAll =
                                    (from pc in sliced
                                     join p in productsAll on pc.ProductId equals p.ProductId
                                     select new ProductWithPriceDto
                                     {
                                         ProductId = p.ProductId,
                                         ProductName = p.ProductName,
                                         Description = p.ProductDescription,
                                         ImageUrl = p.ImageUrl,
                                         SupplierId = p.SupplierId,
                                         Price = pc.Price,
                                         Cost = pc.Cost,
                                         ProductCategoryId = pc.ProductCategoryId
                                     }).ToList();

                                vars["last"] = joinedAll;
                                break;
                            }

                            // --- non-ordinal path with fuzzy keyword rerank ---
                            var best = rows
                                .GroupBy(pc => pc.ProductId)
                                .Select(g => g.OrderBy(x => x.Price).ThenBy(x => x.ProductCategoryId).First())
                                .ToList();

                            if (kws.Count > 0 && best.Count > 0)
                            {
                                var pidSet = best.Select(b => b.ProductId).Distinct().ToList();
                                var products = await _sql.GetProductsByIdsAsync(pidSet, ct);

                                var tokenMap = products.ToDictionary(
                                    p => p.ProductId,
                                    p => new HashSet<string>(Tokenize($"{p.ProductName} {p.ProductDescription}"))
                                );

                                var df = new Dictionary<string, int>();
                                foreach (var kw in kws)
                                {
                                    int count = 0;
                                    foreach (var kv in tokenMap)
                                        if (kv.Value.Any(t => FuzzyTokenEquals(t, kw))) count++;
                                    df[kw] = count;
                                }

                                int N = tokenMap.Count;
                                var w = df.ToDictionary(
                                    kv => kv.Key,
                                    kv => Math.Log(1.0 + (N + 1.0) / (kv.Value + 1.0))
                                );

                                bool strictMode = true; // flip to false if you want softer rerank

                                // common/generic words we don’t want to keep matches for in strict mode
                                var generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                    {
                                        "onesie","shirt","pants","item","items","product","products","clothes","clothing"
                                    };


                                best = best
                                    .Select(b => new
                                    {
                                        pc = b,
                                        score = kws.Sum(kw =>
                                            (tokenMap.TryGetValue(b.ProductId, out var toks) &&
                                             toks.Any(t => FuzzyTokenEquals(t, kw)))
                                                ? w[kw]
                                                : 0.0)
                                    })
                                    .Where(x =>
                                    {
                                        if (!strictMode) return true; // soft mode: keep anything with a score

                                        var allGeneric = kws.Count > 0 && kws.All(k => generic.Contains(k));
                                        var toks = tokenMap.TryGetValue(x.pc.ProductId, out var set) ? set : new HashSet<string>();

                                        if (allGeneric)
                                        {
                                            // Generic-only query (e.g., "onesie"): require a fuzzy match to ANY generic kw
                                            return kws.Any(kw => toks.Any(t => FuzzyTokenEquals(t, kw)));
                                        }

                                        // Mixed query (e.g., "dinosaur onesie"): require a fuzzy match to at least one NON-generic kw
                                        return kws.Any(kw => !generic.Contains(kw) && toks.Any(t => FuzzyTokenEquals(t, kw)));
                                    })

                                    .OrderByDescending(x => x.score)
                                    .ThenBy(x => x.pc.Price)
                                    .ThenBy(x => x.pc.ProductCategoryId)
                                    .Select(x => x.pc)
                                    .ToList();
                            }

                            if (offset > 0) best = best.Skip(offset).ToList();
                            if (limit is > 0) best = best.Take(limit.Value).ToList();

                            var pids2 = best.Select(b => b.ProductId).Distinct().ToList();
                            var products2 = await _sql.GetProductsByIdsAsync(pids2, ct);

                            var joined =
                                (from b in best
                                 join p in products2 on b.ProductId equals p.ProductId
                                 select new ProductWithPriceDto
                                 {
                                     ProductId = p.ProductId,
                                     ProductName = p.ProductName,
                                     Description = p.ProductDescription,
                                     ImageUrl = p.ImageUrl,
                                     SupplierId = p.SupplierId,
                                     Price = b.Price,
                                     Cost = b.Cost,
                                     ProductCategoryId = b.ProductCategoryId
                                 }).ToList();

                            vars["last"] = joined;
                            break;
                        }

                        // ---- supplier path ----
                        // (unchanged from your file)
                        if (entity.Equals("supplier", StringComparison.OrdinalIgnoreCase))
                        {
                            // ...
                        }

                        vars["last"] = Array.Empty<object>();
                        break;
                    }
            }
        }

        return vars.TryGetValue("last", out var lastObj) ? lastObj! : Array.Empty<object>();
    }
}