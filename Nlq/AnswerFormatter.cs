using System;
using System.Globalization;
using System.Reflection;

namespace dataAccess.Planning.Nlq;

public sealed class AnswerFormatter
{
    private static string Peso(decimal v) =>
        "₱" + v.ToString("#,0.##", CultureInfo.InvariantCulture);

    // Return non-nullable T and accept a concrete fallback.
    private static T Get<T>(object row, string prop, T fallback)
    {
        var p = row.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (p == null) return fallback;
        var val = p.GetValue(row);
        if (val is null) return fallback;

        try
        {
            // Handle nullable wrapper types by converting to underlying type then back.
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            var converted = Convert.ChangeType(val, target, CultureInfo.InvariantCulture);
            return (T)(converted is null ? fallback! : converted);
        }
        catch
        {
            return fallback;
        }
    }

    public string ExpenseSummaryOneLiner(object row, string periodLabel)
    {
        var total = Get(row, "total", 0m);
        var prev = Get(row, "prev_total", 0m);
        var pct = Get(row, "delta_pct", 0m);

        if (prev == 0)
            return $"Total expense {periodLabel}: **{Peso(total)}** (no prior-period data).";

        var sign = pct >= 0 ? "▲" : "▼";
        return $"Total expense {periodLabel}: **{Peso(total)}** ({sign} {Math.Abs(pct):0.##}% vs prior).";
    }

    public string SalesSummaryOneLiner(object row, string periodLabel, string metric = "revenue")
    {
        var revenue = Get(row, "revenue", 0m);
        var orders = Get(row, "orders", 0);
        var units = Get(row, "units", 0);
        var aov = Get(row, "avg_order_value", 0m);
        var prev = Get(row, "prev_revenue", 0m);
        var pct = Get(row, "delta_pct", 0m);

        string headline = metric switch
        {
            "orders" => $"Orders {periodLabel}: **{orders:n0}**.",
            "units" => $"Units sold {periodLabel}: **{units:n0}**.",
            "avg_order_value" => $"Avg order value {periodLabel}: **{Peso(aov)}**.",
            _ => $"Revenue {periodLabel}: **{Peso(revenue)}**."
        };

        if (prev == 0) return headline + " (no prior-period revenue).";

        var sign = pct >= 0 ? "▲" : "▼";
        return headline + $" ({sign} {Math.Abs(pct):0.##}% vs prior).";
    }

    public string SalesTopProductOneLiner(object? rowsObj, string periodLabel)
    {
        if (rowsObj is System.Collections.IEnumerable seq)
        {
            foreach (var item in seq)
            {
                var name = Get(item, "product", "—");
                var revenue = Get(item, "revenue", 0m);
                var units = Get(item, "units", 0);
                return $"Top product {periodLabel}: **{name}** — {units:n0} units, {Peso(revenue)} revenue.";
            }
        }
        return $"No sold products {periodLabel}.";
    }
}
