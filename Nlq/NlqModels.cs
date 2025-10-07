using System;
using System.Collections.Generic;

namespace dataAccess.Planning.Nlq;

public record NlqPlan
{
    public string Mode { get; init; } = "answer";                    // "answer" | "report"
    public List<string> Domains { get; init; } = new();              // ["expense"] | ["sales"] | ["inventory"] | []
    public string? Metric { get; init; }                              // expense: total_expense; sales: revenue|orders|units|avg_order_value|top_product; inventory: summary
    public TimeSpec Time { get; init; } = new();
    public List<FilterSpec>? Filters { get; init; }
    public bool CompareToPrior { get; init; }
    public double Confidence { get; init; } = 0.75;
    public Dictionary<string, object?>? Entities { get; set; }
}

public sealed record TimeSpec
{
    public string Preset { get; init; } = "";                        // this_week|last_week|this_month|last_month|as_of|range
    public string? Start { get; init; }                               // YYYY-MM-DD (for range/as_of)
    public string? End { get; init; }                                 // YYYY-MM-DD
    public string? Grain { get; init; }
    public Dictionary<string, object?>? Entities { get; set; }
}

public sealed record FilterSpec(string Field, string Op, string? Value);

public sealed record NlqResolvedPlan : NlqPlan
{
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public DateOnly? PriorStart { get; init; }
    public DateOnly? PriorEnd { get; init; }

    public string Domain => Domains.Count > 0 ? Domains[0] : "";
}
