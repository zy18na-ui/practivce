using System;
using System.Collections.Generic;
using System.Linq;

namespace dataAccess.Reports;

public static class DerivedStats
{
    public static double DeltaPct(decimal current, decimal previous)
    {
        if (previous == 0m) return 0.0;
        return (double)((current - previous) / previous);
    }

    // Simple IQR spike detector on daily totals
    public static IReadOnlyList<DateOnly> FindSpikes(IEnumerable<(DateOnly day, decimal value)> points)
    {
        var vals = points.Select(p => p.value).OrderBy(v => v).ToList();
        if (vals.Count < 8) return Array.Empty<DateOnly>();

        decimal Q1 = Percentile(vals, 0.25m);
        decimal Q3 = Percentile(vals, 0.75m);
        decimal iqr = Q3 - Q1;
        decimal upper = Q3 + 1.5m * iqr;

        return points.Where(p => p.value > upper).Select(p => p.day).ToList();
    }

    public static decimal Percentile(List<decimal> sorted, decimal p)
    {
        if (sorted.Count == 0) return 0m;
        var n = sorted.Count;
        var rank = (n - 1) * p + 1;
        var k = (int)Math.Floor((double)rank) - 1;
        var d = (decimal)rank - (k + 1);
        if (k < 0) return sorted[0];
        if (k + 1 >= n) return sorted[n - 1];
        return sorted[k] + d * (sorted[k + 1] - sorted[k]);
    }
}
