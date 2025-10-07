using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dataAccess.Planning;

public sealed class QueryPlan
{
    public List<object> Plan { get; set; } = new();
}

// ---- Ops ----
public class PlanOp { public string Op { get; set; } = ""; }

public class VectorSearchOp : PlanOp
{
    public string Entity { get; set; } = "";
    public string Text { get; set; } = "";
    public int Topk { get; set; } = 10;
    public string Return { get; set; } = "ids";
}

public class SelectOp : PlanOp
{
    public string Entity { get; set; } = "";
    public List<Filter> Filters { get; set; } = new();
    public string? IdsIn { get; set; }
    public List<string> Project { get; set; } = new();
    public List<SortKey> Sort { get; set; } = new();
    public int? Limit { get; set; }
}

public class JoinOp : PlanOp
{
    public EntityRef From { get; set; } = new();
    public EntityRef To { get; set; } = new();
    public JoinOn On { get; set; } = new();
    public string? IdsIn { get; set; }
    public List<string> Project { get; set; } = new();
}

public class AggregateOp : PlanOp
{
    public string Entity { get; set; } = "";
    public List<string> GroupBy { get; set; } = new();
    public List<Metric> Metrics { get; set; } = new();
}

// ---- Supporting types ----
public class Filter { public string Field { get; set; } = ""; public string Op { get; set; } = ""; public object? Value { get; set; } public string? ValueVar { get; set; } }
public class SortKey { public string Field { get; set; } = ""; public string Dir { get; set; } = "asc"; }
public class EntityRef { public string Entity { get; set; } = ""; }
public class JoinOn { public string FromField { get; set; } = ""; public string ToField { get; set; } = ""; }
public class Metric { public string Field { get; set; } = ""; public string Agg { get; set; } = "min"; public string As { get; set; } = "value"; }


