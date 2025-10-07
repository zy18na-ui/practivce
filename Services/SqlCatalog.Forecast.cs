using System.Data;
using Microsoft.EntityFrameworkCore;

namespace dataAccess.Services; // ← same as SqlCatalog.cs

public sealed partial class SqlCatalog
{
    // SALES_DAILY_SERIES(start,end) → [ { date, value } ]
    public async Task<IReadOnlyList<(DateOnly Date, decimal Value)>> GetSalesDailySeriesAsync(
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        const string sql = @"
            SELECT o.orderdate::date AS d, SUM(o.totalamount) AS v
            FROM public.orders o
            WHERE o.orderdate >= @start
              AND o.orderdate < (@end + INTERVAL '1 day')
            GROUP BY o.orderdate::date
            ORDER BY d;";

        return await QueryDailyAsync(sql, start, end, ct);
    }

    // EXPENSES_DAILY_SERIES(start,end) → [ { date, value } ]
    public async Task<IReadOnlyList<(DateOnly Date, decimal Value)>> GetExpensesDailySeriesAsync(
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        const string sql = @"
            SELECT e.occurred_on::date AS d, SUM(e.amount) AS v
            FROM public.expenses e
            WHERE e.occurred_on >= @start
              AND e.occurred_on < (@end + INTERVAL '1 day')
            GROUP BY e.occurred_on::date
            ORDER BY d;";

        return await QueryDailyAsync(sql, start, end, ct);
    }

    // EF Core-safe helper (NO CreateCommand on DbContext, NO AddDate)
    private async Task<IReadOnlyList<(DateOnly, decimal)>> QueryDailyAsync(
        string sql, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var results = new List<(DateOnly, decimal)>();
        var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var pStart = cmd.CreateParameter();
        pStart.ParameterName = "@start";
        pStart.DbType = DbType.Date;
        pStart.Value = start.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pStart);

        var pEnd = cmd.CreateParameter();
        pEnd.ParameterName = "@end";
        pEnd.DbType = DbType.Date;
        pEnd.Value = end.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add(pEnd);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            // Col0: date; Col1: numeric sum (may be null)
            var dt = r.GetFieldValue<DateTime>(0);
            var vObj = r.GetValue(1);
            var v = (vObj == DBNull.Value) ? 0m : Convert.ToDecimal(vObj);
            results.Add((DateOnly.FromDateTime(dt), v));
        }
        return results;
    }
}
