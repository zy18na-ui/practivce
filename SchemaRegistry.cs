using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace dataAccess.Planning;

public sealed class Registry
{
    public Dictionary<string, EntityDef> Entities { get; set; } = new();
    public List<RelationDef> Relations { get; set; } = new();
    public Dictionary<string, List<string>> Synonyms { get; set; } = new();
    public GuardrailDef Guardrails { get; set; } = new();
}

public sealed class EntityDef
{
    public string PrimaryKey { get; set; } = "";
    public List<string> Fields { get; set; } = new();
}

public sealed class RelationDef
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string FromField { get; set; } = "";
    public string ToField { get; set; } = "";
}

public sealed class GuardrailDef
{
    public int MaxLimit { get; set; } = 50;
    public List<string> AllowOps { get; set; } = new();
}

