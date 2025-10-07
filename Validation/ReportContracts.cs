public record SqlRequest(string QueryId, IDictionary<string, object?> Args);
public record Phase1Plan(string Intent, IDictionary<string, object?> Slots, IReadOnlyList<SqlRequest> SqlRequests);

public record Kpi(string Label, decimal Value, decimal? Delta_Pct_Vs_Prev);
public record CardItem(string Name, decimal Amount, decimal Share_Pct, string? Note);
public record Card(string Title, IReadOnlyList<CardItem> Items);
public record ChartSeries(string Name, IReadOnlyList<decimal> Data);
public record Chart(string Type, string Title, string X, IReadOnlyList<ChartSeries> Series);
public record UiSpec(string Report_Title, Period Period, IReadOnlyList<Kpi> Kpis,
                     IReadOnlyList<Card> Cards, IReadOnlyList<Chart> Charts,
                     IReadOnlyList<string> Narrative, IReadOnlyList<ActionSpec> Actions);
public record Period(string Label);
public record ActionSpec(string Id, string Label);
