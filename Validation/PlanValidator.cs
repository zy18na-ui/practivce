using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace dataAccess.Planning.Validation;

public sealed class PlanValidator
{
    private readonly Registry _reg;

    public PlanValidator(Registry reg) { _reg = reg; }

    public (bool ok, string? error, QueryPlan plan) Validate(QueryPlan plan)
    {
        return (true, null, plan);
    }

    public void ValidatePhase1(JsonDocument doc)
    {
        var root = doc.RootElement;
        Require(root, "intent");
        Require(root, "slots");
        Require(root, "sql_requests");
        foreach (var sr in root.GetProperty("sql_requests").EnumerateArray())
        {
            Require(sr, "query_id");
            Require(sr, "args");
        }
    }

    public void ValidateUiSpec(JsonDocument ui, IDictionary<string, object?> rows)
    {
        var r = ui.RootElement;
        foreach (var key in new[] { "report_title", "period", "kpis", "cards", "charts", "narrative", "actions" })
            Require(r, key);

        // Example “no invented numbers”: ensure KPI total equals rows from EXPENSE_SUMMARY
        if (rows.TryGetValue("EXPENSE_SUMMARY", out var summary) && summary is not null)
        {
            var kpi = r.GetProperty("kpis")[0];
            var kpiVal = kpi.GetProperty("value").GetDecimal();
            var total = GetDecimal(summary, "total");
            if (Math.Abs(kpiVal - total) > 0.01m)
                throw new ValidationException($"KPI Total ({kpiVal}) does not match SQL rows total ({total}).");
        }
        // narrative must be an array with >= 2 non-empty strings
        if (!r.TryGetProperty("narrative", out var narrEl))
            throw new ValidationException("UI spec missing 'narrative'.");

        if (narrEl.ValueKind != JsonValueKind.Array)
            throw new ValidationException("'narrative' must be an array of strings.");

        int nonEmpty = 0;
        foreach (var s in narrEl.EnumerateArray())
        {
            if (s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
                nonEmpty++;
        }
        if (nonEmpty < 2)
            throw new ValidationException("'narrative' must contain at least two non-empty sentences.");
    }

    private static void Require(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out _)) throw new ValidationException($"Missing required field: {name}");
    }

    // helper to pull decimals from an anonymous rows object
    private static decimal GetDecimal(object o, string prop)
    {
        var p = o.GetType().GetProperty(prop);
        if (p is null) throw new ValidationException($"Rows missing '{prop}'.");
        return Convert.ToDecimal(p.GetValue(o) ?? 0);
    }
}


