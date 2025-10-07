// dataAccess/Services/SimpleForecastService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace dataAccess.Services;

public enum ForecastDomain { Sales, Expenses }
public sealed record ForecastPoint(DateOnly Date, decimal Value, decimal Lower, decimal Upper);

/// <summary>
/// Simple deterministic forecasting (CMA trend + weekday seasonality + CI band).
/// Works with either:
///   A) New QIDs: ISqlCatalog.GetSalesDailySeriesAsync / GetExpensesDailySeriesAsync
///   B) Existing QIDs via RunAsync: "SALES_BY_DAY" / "EXPENSE_BY_DAY"
/// </summary>
public sealed class SimpleForecastService
{
    private readonly ISqlCatalog _sql;

    public SimpleForecastService(ISqlCatalog sql) => _sql = sql;

    public async Task<object> ForecastAsync(
        ForecastDomain domain, int days = 30,
        DateOnly? from = null, DateOnly? to = null,
        CancellationToken ct = default)
    {
        var today = TodayPH();
        var histEnd = to ?? today;
        var histStart = from ?? histEnd.AddDays(-180);

        var history = domain == ForecastDomain.Sales
            ? await LoadDailySeriesSales(histStart, histEnd, ct)
            : await LoadDailySeriesExpenses(histStart, histEnd, ct);

        history = FillGaps(histStart, histEnd, history);
        var values = history.Select(x => x.value).ToArray();

        if (history.Count < 21)
        {
            var ma7 = Rolling(values, 7);
            var last = (decimal)ma7[^1];
            var fc = BuildForecast(histEnd, days, last, wf: null, sigma: 0m);
            return Wrap(history, fc, histEnd, days, residuals: null);
        }

        int window = history.Count >= 56 ? 28 : 14;
        var trend = CenteredMA(values, window);
        ReplaceNaWithRolling(values, trend, Math.Max(7, window / 2));

        decimal[]? weekday = history.Count >= 42 ? WeekdayFactors(history, trend) : null;

        var fitted = Fit(values, trend, weekday, history);
        var residuals = values.Zip(fitted, (y, yh) => (double)((decimal)y - (decimal)yh)).ToArray();
        var sigma = Std(residuals);

        var lastTrend = (decimal)trend[^1];
        var fcPoints = BuildForecast(histEnd, days, lastTrend, weekday, (decimal)sigma);

        return Wrap(history, fcPoints, histEnd, days, residuals);
    }

    // ---------- loaders (QID method if present; else RunAsync fallback) ----------
    private async Task<List<(DateOnly date, decimal value)>> LoadDailySeriesSales(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var mi = _sql.GetType().GetMethod("GetSalesDailySeriesAsync");
        if (mi != null)
        {
            var task = (Task)mi.Invoke(_sql, new object?[] { start, end, ct })!;
            await task.ConfigureAwait(false);
            var result = (IReadOnlyList<(DateOnly Date, decimal Value)>)task.GetType().GetProperty("Result")!.GetValue(task)!;
            return result.Select(x => (x.Date, x.Value)).ToList();
        }

        var rows = (IEnumerable<dynamic>)(await _sql.RunAsync("SALES_BY_DAY",
            new Dictionary<string, object?> { ["start"] = start, ["end"] = end }, ct))!;
        return rows.Select(r => ((DateOnly)r.date, (decimal)r.revenue)).ToList();
    }

    private async Task<List<(DateOnly date, decimal value)>> LoadDailySeriesExpenses(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var mi = _sql.GetType().GetMethod("GetExpensesDailySeriesAsync");
        if (mi != null)
        {
            var task = (Task)mi.Invoke(_sql, new object?[] { start, end, ct })!;
            await task.ConfigureAwait(false);
            var result = (IReadOnlyList<(DateOnly Date, decimal Value)>)task.GetType().GetProperty("Result")!.GetValue(task)!;
            return result.Select(x => (x.Date, x.Value)).ToList();
        }

        var rows = (IEnumerable<dynamic>)(await _sql.RunAsync("EXPENSE_BY_DAY",
            new Dictionary<string, object?> { ["start"] = start, ["end"] = end }, ct))!;
        return rows.Select(r => ((DateOnly)r.date, (decimal)r.total)).ToList();
    }

    // ---------- helpers ----------
    private static DateOnly TodayPH()
    {
        string[] ids = OperatingSystem.IsWindows()
            ? new[] { "Singapore Standard Time", "Taipei Standard Time", "Malay Peninsula Standard Time" }
            : new[] { "Asia/Manila", "Asia/Singapore", "Asia/Taipei" };

        TimeZoneInfo? tz = null;
        foreach (var id in ids) { try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); break; } catch { } }
        tz ??= TimeZoneInfo.Local;
        var nowPh = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return DateOnly.FromDateTime(nowPh.Date);
    }

    private static List<(DateOnly date, decimal value)> FillGaps(DateOnly s, DateOnly e, IReadOnlyList<(DateOnly date, decimal value)> raw)
    {
        var d = raw.ToDictionary(x => x.date, x => x.value);
        var L = new List<(DateOnly, decimal)>();
        for (var dt = s; dt <= e; dt = dt.AddDays(1))
            L.Add((dt, d.TryGetValue(dt, out var v) ? v : 0m));
        return L;
    }

    private static double[] CenteredMA(decimal[] arr, int w)
    {
        var a = arr.Select(x => (double)x).ToArray();
        var n = a.Length; var res = new double[n]; Array.Fill(res, double.NaN);
        int h = w / 2;
        for (int i = h; i < n - h; i++)
        {
            double s = 0;
            for (int j = i - h; j <= i + h - (w % 2 == 0 ? 1 : 0); j++) s += a[j];
            res[i] = s / w;
        }
        return res;
    }

    private static double[] Rolling(decimal[] arr, int w)
    {
        var a = arr.Select(x => (double)x).ToArray();
        var n = a.Length; var res = new double[n];
        for (int i = 0; i < n; i++)
        {
            int s = Math.Max(0, i - w + 1);
            double sum = 0; int c = 0;
            for (int j = s; j <= i; j++) { sum += a[j]; c++; }
            res[i] = sum / Math.Max(1, c);
        }
        return res;
    }

    private static void ReplaceNaWithRolling(decimal[] vals, double[] trend, int w)
    {
        var roll = Rolling(vals, w);
        for (int i = 0; i < trend.Length; i++)
            if (double.IsNaN(trend[i])) trend[i] = roll[i];
    }

    private static decimal[] WeekdayFactors(List<(DateOnly date, decimal value)> hist, double[] trend)
    {
        var buckets = Enumerable.Range(0, 7).Select(_ => new List<double>()).ToArray();
        for (int i = 0; i < hist.Count; i++)
        {
            if (trend[i] <= 0 || double.IsNaN(trend[i])) continue;
            var ratio = (double)hist[i].value / trend[i];
            buckets[(int)hist[i].date.DayOfWeek].Add(ratio);
        }
        var f = new decimal[7]; double sum = 0;
        for (int d = 0; d < 7; d++)
        {
            var m = buckets[d].Count == 0 ? 1.0 : buckets[d].Average();
            m = Math.Clamp(m, 0.6, 1.4);
            f[d] = (decimal)m; sum += m;
        }
        var norm = (decimal)(sum / 7.0);
        for (int d = 0; d < 7; d++) f[d] /= norm;
        return f;
    }

    private static double[] Fit(decimal[] y, double[] trend, decimal[]? wf, List<(DateOnly date, decimal value)> hist)
    {
        var f = new double[y.Length];
        for (int i = 0; i < y.Length; i++)
        {
            var baseV = double.IsNaN(trend[i]) ? (double)hist[i].value : trend[i];
            if (wf is not null) baseV *= (double)wf[(int)hist[i].date.DayOfWeek];
            f[i] = Math.Max(0, baseV);
        }
        return f;
    }

    private static List<ForecastPoint> BuildForecast(DateOnly lastHist, int days, decimal lastTrend, decimal[]? wf, decimal sigma)
    {
        var L = new List<ForecastPoint>(days);
        for (int i = 1; i <= days; i++)
        {
            var d = lastHist.AddDays(i);
            decimal v = lastTrend;
            if (wf is not null) v *= wf[(int)d.DayOfWeek];
            v = Math.Max(0m, decimal.Round(v, 2));
            var s = decimal.Round(1.28m * sigma, 2);
            L.Add(new(d, v, Math.Max(0m, v - s), v + s));
        }
        return L;
    }

    private static double Std(double[] x)
    {
        if (x.Length == 0) return 0;
        var m = x.Average();
        var v = x.Sum(vv => (vv - m) * (vv - m)) / Math.Max(1, x.Length - 1);
        return Math.Sqrt(v);
    }

    private static object Wrap(List<(DateOnly date, decimal value)> hist, List<ForecastPoint> fc, DateOnly end, int days, double[]? residuals)
    {
        decimal Sum(IEnumerable<decimal> xs) => xs.Aggregate(0m, (a, b) => a + b);
        var last7 = Sum(hist.TakeLast(7).Select(x => x.value));
        var last28 = Sum(hist.TakeLast(28).Select(x => x.value));
        var sumFc = Sum(fc.Select(x => x.Value));

        double? mape = null;
        if (residuals is { Length: >= 7 })
        {
            var y = hist.Select(h => (double)h.value).ToArray();
            var fitted = y.Zip(residuals, (yy, e) => yy - e).ToArray();
            int n = 0; double ape = 0;
            for (int i = y.Length - 7; i < y.Length; i++)
            {
                if (y[i] <= 0.0) continue;
                ape += Math.Abs((y[i] - fitted[i]) / y[i]); n++;
            }
            if (n > 0) mape = (ape / n) * 100.0;
        }

        return new
        {
            series = new
            {
                history = hist.Select(x => new { date = x.date, value = x.value }),
                forecast = fc.Select(x => new { date = x.Date, value = x.Value, lower = x.Lower, upper = x.Upper })
            },
            kpis = new
            {
                horizon_days = days,
                sum_forecast = sumFc,
                last_7d_actual = last7,
                last_28d_actual = last28,
                backtest_mape_7d = mape
            },
            period = new { label = $"{end.AddDays(1):MMM d}–{end.AddDays(days):MMM d, yyyy}" }
        };
    }
}
