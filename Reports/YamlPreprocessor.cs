using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace dataAccess.Reports;

public sealed class YamlPreprocessor
{
    private readonly DateRangeResolver _range;

    public YamlPreprocessor(DateRangeResolver range) => _range = range;

    public (bool Allowed, string? Message, Hints Data) Prepare(string domain, string userText)
    {
        var txt = (userText ?? string.Empty).Trim();

        // Inventory comparisons are blocked
        var compare = _range.WantsCompare(txt);
        if (domain == "inventory" && compare)
            return (false, "Inventory is a snapshot (no comparisons). Try 'inventory summary for today.'", Hints.Empty);

        // Time window
        var (start, end, label, prevStart, prevEnd, preset) = _range.Resolve(txt);

        // Lightweight tag parsing for scope/product/topk
        // Accepted tags (case-insensitive):
        //   [SCOPE=item] | [SCOPE=overall]
        //   [PRODUCT_ID=xyz123]
        //   [TOPK=5]
        string? scope = null;
        string? productId = null;
        int? topK = null;

        var mScope = Regex.Match(txt, @"\[SCOPE\s*=\s*(item|overall)\]", RegexOptions.IgnoreCase);
        if (mScope.Success) scope = mScope.Groups[1].Value.ToLowerInvariant();

        var mPid = Regex.Match(txt, @"\[PRODUCT_ID\s*=\s*([^\]\s]+)\]", RegexOptions.IgnoreCase);
        if (mPid.Success) productId = mPid.Groups[1].Value;

        var mTopk = Regex.Match(txt, @"\[TOPK\s*=\s*(\d{1,3})\]", RegexOptions.IgnoreCase);
        if (mTopk.Success && int.TryParse(mTopk.Groups[1].Value, out var k) && k > 0) topK = k;

        // If product is present but scope missing → assume item
        if (productId is not null && string.IsNullOrWhiteSpace(scope))
            scope = "item";

        // Default scope for sales: overall
        if (domain == "sales" && string.IsNullOrWhiteSpace(scope))
            scope = "overall";

        return (true, null, new Hints
        {
            Start = start,
            End = end,
            Label = label,
            TimePreset = preset,
            CompareToPrior = compare,
            PrevStart = compare ? prevStart : null,
            PrevEnd = compare ? prevEnd : null,
            Filters = null,
            UserId = null,
            Scope = scope,
            ProductId = productId,
            TopK = topK
        });
    }

    public sealed class Hints
    {
        public static readonly Hints Empty = new();
        public string? Start { get; init; }
        public string? End { get; init; }
        public string? Label { get; init; }
        public string? TimePreset { get; init; }
        public bool CompareToPrior { get; init; }
        public string? PrevStart { get; init; }
        public string? PrevEnd { get; init; }
        public object? Filters { get; init; }
        public string? UserId { get; init; }

        // NEW: sales scope hints
        public string? Scope { get; init; }          // "overall" | "item"
        public string? ProductId { get; init; }      // required if Scope=item
        public int? TopK { get; init; }              // optional for best-sellers/variants
    }
}
