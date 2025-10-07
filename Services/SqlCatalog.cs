using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json; // NEW
using System.Threading.Tasks;

namespace dataAccess.Services
{
    public interface ISqlCatalog
    {
        Task<object?> RunAsync(string queryId, IDictionary<string, object?> args, CancellationToken ct = default);
    }

    public sealed partial class SqlCatalog : ISqlCatalog
    {
        private readonly AppDbContext _db;
        public SqlCatalog(AppDbContext db) => _db = db;

        // ---------- helpers: unwrap & parse ----------
        private static object? Unwrap(object? v)
        {
            if (v is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString(),
                    JsonValueKind.Number => je.TryGetInt64(out var l) ? l : (object?)je.ToString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => je.ToString()
                };
            }
            return v;
        }

        private static DateOnly GetDateArg(IDictionary<string, object?> a, string key)
        {
            if (!a.TryGetValue(key, out var v) || v is null)
                throw new ArgumentException($"Missing required date arg '{key}'");

            v = Unwrap(v);

            if (v is DateOnly d) return d;
            if (v is DateTime dt) return DateOnly.FromDateTime(dt);
            if (v is string s && DateOnly.TryParse(s, out var ds)) return ds;
            if (v is long epoch) return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(epoch).DateTime);

            throw new InvalidCastException($"Arg '{key}' must be ISO date (yyyy-MM-dd). Got {v.GetType().Name}.");
        }

        private static Guid? GetGuidArg(IDictionary<string, object?> a, string key)
        {
            if (!a.TryGetValue(key, out var v) || v is null) return null;
            v = Unwrap(v);
            if (v is Guid g) return g;
            if (v is string s && Guid.TryParse(s, out var gs)) return gs;
            return null;
        }

        private static int GetIntArg(IDictionary<string, object?> a, string key, int @default = 0)
        {
            if (!a.TryGetValue(key, out var v) || v is null) return @default;
            v = Unwrap(v);
            if (v is int i) return i;
            if (v is long l) return checked((int)l);
            if (v is string s && int.TryParse(s, out var si)) return si;
            throw new InvalidCastException($"Arg '{key}' must be int. Got {v.GetType().Name}.");
        }

        // Force DateOnly → UTC DateTime (for timestamptz columns)
        private static DateTime ToDbStart(DateOnly d) =>
            DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

        private static DateTime ToDbEnd(DateOnly d) =>
            DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Unspecified);

        // --------------------------------------------

        public Task<object?> RunAsync(string queryId, IDictionary<string, object?> args, CancellationToken ct = default) =>
            queryId switch
            {
                "EXPENSE_SUMMARY" => ExpenseSummary(args, ct),
                "TOP_EXPENSE_CATEGORIES" => TopExpenseCategories(args, ct),
                "EXPENSE_BY_CATEGORY_WEEKLY" => ExpenseByCategoryWeekly(args, ct),
                "EXPENSE_BY_DAY" => ExpenseByDay(args, ct),
                "EXPENSE_BY_CATEGORY" => ExpenseByCategory(args, ct),
                "EXPENSE_BY_SUPPLIER" => ExpenseBySupplier(args, ct),
                "TOP_EXPENSE_SUPPLIERS" => TopExpenseSuppliers(args, ct),
                "EXPENSE_RECENT_TRANSACTIONS" => ExpenseRecentTransactions(args, ct),
                "EXPENSE_LABEL_BREAKDOWN" => ExpenseLabelBreakdown(args, ct),
                "EXPENSE_BUDGET_VS_ACTUAL" => ExpenseBudgetVsActual(args, ct),
                "EXPENSE_NOTES_SEARCH" => ExpenseNotesSearch(args, ct),
                //"BUDGET_UTILIZATION" => ExpenseBudgetVsActualCompat(args, ct),

                // --- NEW: INVENTORY ---
                "INVENTORY_SNAPSHOT" => InventorySnapshot(args, ct),
                "LOW_STOCK_ITEMS" => LowStockItems(args, ct),
                "STOCK_BY_CATEGORY" => StockByCategory(args, ct),
                "STOCK_MOVEMENT_WEEKLY" => StockMovementWeekly(args, ct),
                "INV_SNAPSHOT" => InvSnapshot(args, ct),
                "INV_BY_PRODUCT" => InvByProduct(args, ct),
                "INV_LOW_STOCK" => InvLowStock(args, ct),
                "INV_OUT_OF_STOCK" => InvOutOfStock(args, ct),
                "INV_VALUATION_CURRENT" => InvValuationCurrent(args, ct),
                "INV_AVAILABLE_PRODUCTS" => InvAvailableProducts(args, ct),

                // --- NEW: SALES ---
                "SALES_SUMMARY" => SalesSummary(args, ct),
                "TOP_PRODUCTS" => TopProducts(args, ct),
                "SALES_BY_DAY" => SalesByDay(args, ct),
                "SALES_BY_WEEK" => SalesByWeek(args, ct),
                "SALES_BY_HOUR" => SalesByHour(args, ct),
                "SALES_BY_PRODUCT" => SalesByProduct(args, ct),
                "SALES_BY_CATEGORY" => SalesByCategory(args, ct),
                "BOTTOM_PRODUCTS" => BottomProducts(args, ct),
                "ORDERS_BY_STATUS" => OrdersByStatus(args, ct),
                "AOV_BY_DAY" => AovByDay(args, ct),
                "AOV_BY_WEEK" => AovByWeek(args, ct),
                "ORDER_SIZE_DISTRIBUTION" => OrderSizeDistribution(args, ct),
                "RECENT_ORDERS" => RecentOrders(args, ct),
                "ORDER_DETAIL" => OrderDetail(args, ct),
                "SALES_BY_PRODUCT_DAY" => SalesByProductDay(args, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(queryId), $"Unknown query_id {queryId}")
            };

        private async Task<object?> ExpenseSummary(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var userId = GetGuidArg(a, "user_id"); // optional

            var q = _db.Expenses.Where(e =>
                e.OccurredOn.HasValue &&
                e.OccurredOn.Value >= start &&
                e.OccurredOn.Value <= end &&
                (!userId.HasValue || e.UserId == userId));

            decimal total = await q.SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

            var days = end.ToDateTime(TimeOnly.MinValue) - start.ToDateTime(TimeOnly.MinValue);
            var prevEnd = start.AddDays(-1);
            var prevStart = DateOnly.FromDateTime(prevEnd.ToDateTime(TimeOnly.MinValue) - days);

            var qPrev = _db.Expenses.Where(e =>
                e.OccurredOn.HasValue &&
                e.OccurredOn.Value >= prevStart &&
                e.OccurredOn.Value <= prevEnd &&
                (!userId.HasValue || e.UserId == userId));

            decimal prev = await qPrev.SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

            var deltaPct = prev == 0 ? 0 : Math.Round(((total - prev) / prev) * 100m, 2);
            return new { total = Math.Round(total, 2), prev_total = Math.Round(prev, 2), delta_pct = deltaPct };
        }

        private async Task<object?> TopExpenseCategories(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var k = GetIntArg(a, "k", 5);
            var userId = GetGuidArg(a, "user_id");

            var grouped = await _db.Expenses
                .Where(e =>
                    e.OccurredOn.HasValue &&
                    e.OccurredOn.Value >= start &&
                    e.OccurredOn.Value <= end &&
                    e.CategoryId.HasValue &&
                    (!userId.HasValue || e.UserId == userId))
                .Join(_db.Categories,
                      e => e.CategoryId!.Value,
                      c => c.Id,
                      (e, c) => new { c.Name, e.Amount })
                .GroupBy(x => x.Name)
                .Select(g => new { name = g.Key, amount = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.amount)
                .ToListAsync(ct);

            var total = grouped.Sum(x => x.amount);
            return grouped.Take(k).Select(x => new {
                name = x.name,
                amount = Math.Round(x.amount, 2),
                share_pct = total == 0 ? 0 : Math.Round((x.amount / total) * 100m, 2),
                note = ""
            }).ToList();
        }

        private async Task<object?> ExpenseByCategoryWeekly(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.Expenses
                .Where(e =>
                    e.OccurredOn.HasValue &&
                    e.OccurredOn.Value >= start &&
                    e.OccurredOn.Value <= end &&
                    e.CategoryId.HasValue &&
                    (!userId.HasValue || e.UserId == userId))
                .Join(_db.Categories,
                      e => e.CategoryId!.Value,
                      c => c.Id,
                      (e, c) => new { c.Name, OccurredOn = e.OccurredOn!.Value, e.Amount })
                .ToListAsync(ct);

            var byCatWeek = rows
                .GroupBy(r => r.Name)
                .Select(g => new {
                    name = g.Key,
                    data = g.GroupBy(r => ISOWeek.GetWeekOfYear(r.OccurredOn.ToDateTime(TimeOnly.MinValue)))
                            .OrderBy(x => x.Key)
                            .Select(x => Math.Round(x.Sum(s => (decimal)s.Amount), 2))
                            .ToList()
                });

            var x = rows.Select(r => ISOWeek.GetWeekOfYear(r.OccurredOn.ToDateTime(TimeOnly.MinValue)))
                        .Distinct().OrderBy(w => w).ToList();

            return new { x, series = byCatWeek.ToList() };
        }
        private async Task<object?> ExpenseByDay(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.Expenses
                .Where(e => e.OccurredOn.HasValue
                    && e.OccurredOn.Value >= start
                    && e.OccurredOn.Value <= end
                    && (!userId.HasValue || e.UserId == userId))
                .GroupBy(e => e.OccurredOn!.Value)
                .Select(g => new { day = g.Key, total = g.Sum(x => x.Amount) })
                .OrderBy(g => g.day)
                .ToListAsync(ct);

            return rows.Select(r => new { date = r.day, total = Math.Round((decimal)r.total, 2) }).ToList();
        }

        private async Task<object?> ExpenseByCategory(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.Expenses
                .Where(e => e.OccurredOn.HasValue
                    && e.OccurredOn.Value >= start
                    && e.OccurredOn.Value <= end
                    && (!userId.HasValue || e.UserId == userId))
                .GroupJoin(_db.Categories, e => e.CategoryId, c => (Guid?)c.Id,
                    (e, cg) => new { e.Amount, Cat = cg.Select(x => x.Name).FirstOrDefault() })
                .GroupBy(x => x.Cat ?? "Uncategorized")
                .Select(g => new { category = g.Key, total = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.total)
                .ToListAsync(ct);

            return rows.Select(r => new { r.category, total = Math.Round((decimal)r.total, 2) }).ToList();
        }

        private async Task<object?> ExpenseBySupplier(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.Expenses
                .Where(e => e.OccurredOn.HasValue
                    && e.OccurredOn.Value >= start
                    && e.OccurredOn.Value <= end
                    && (!userId.HasValue || e.UserId == userId))
                .GroupJoin(_db.Contacts, e => e.ContactId, co => (Guid?)co.Id,
                    (e, cg) => new { e.Amount, Supplier = cg.Select(x => x.Name).FirstOrDefault() })
                .GroupBy(x => x.Supplier ?? "Unknown")
                .Select(g => new { supplier = g.Key, total = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.total)
                .ToListAsync(ct);

            return rows.Select(r => new { r.supplier, total = Math.Round((decimal)r.total, 2) }).ToList();
        }

        private async Task<object?> TopExpenseSuppliers(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var k = GetIntArg(a, "k", 5);
            var userId = GetGuidArg(a, "user_id");

            var grouped = await _db.Expenses
                .Where(e => e.OccurredOn.HasValue
                    && e.OccurredOn.Value >= start
                    && e.OccurredOn.Value <= end
                    && (!userId.HasValue || e.UserId == userId))
                .GroupJoin(_db.Contacts, e => e.ContactId, co => (Guid?)co.Id,
                    (e, cg) => new { e.Amount, Supplier = cg.Select(x => x.Name).FirstOrDefault() })
                .GroupBy(x => x.Supplier ?? "Unknown")
                .Select(g => new { name = g.Key, amount = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.amount)
                .ToListAsync(ct);

            var total = grouped.Sum(x => x.amount);
            return grouped.Take(k).Select(x => new {
                name = x.name,
                amount = Math.Round((decimal)x.amount, 2),
                share_pct = total == 0 ? 0 : Math.Round(((decimal)x.amount / (decimal)total) * 100m, 2),
                note = ""
            }).ToList();
        }

        private async Task<object?> ExpenseRecentTransactions(IDictionary<string, object?> a, CancellationToken ct)
        {
            var n = GetIntArg(a, "n", 20);
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.Expenses
                .Where(e => !userId.HasValue || e.UserId == userId)
                .OrderByDescending(e => e.OccurredOn ?? DateOnly.MinValue)
                .ThenByDescending(e => e.Id)
                .Take(n)
                .GroupJoin(_db.Categories, e => e.CategoryId, c => (Guid?)c.Id,
                    (e, cg) => new
                    {
                        e.OccurredOn,
                        e.Amount,
                        Category = cg.Select(x => x.Name).FirstOrDefault(),
                        e.ContactId,
                        e.Notes
                    })
                .GroupJoin(_db.Contacts, x => x.ContactId, co => (Guid?)co.Id,
                    (x, cog) => new
                    {
                        occurred_on = x.OccurredOn,
                        amount = x.Amount,
                        category = x.Category ?? "Uncategorized",
                        supplier = cog.Select(s => s.Name).FirstOrDefault() ?? "Unknown",
                        notes = x.Notes
                    })
                .ToListAsync(ct);

            return rows.Select(r => new {
                r.occurred_on,
                amount = Math.Round((decimal)r.amount, 2),
                r.category,
                r.supplier,
                r.notes
            }).ToList();
        }

        private async Task<object?> ExpenseLabelBreakdown(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.ExpenseLabels
                .Join(_db.Expenses, el => el.ExpenseId, e => e.Id, (el, e) => new { el, e })
                .Where(x => x.e.OccurredOn.HasValue
                    && x.e.OccurredOn.Value >= start
                    && x.e.OccurredOn.Value <= end
                    && (!userId.HasValue || x.e.UserId == userId))
                .Join(_db.Labels, x => x.el.LabelId, l => l.Id, (x, l) => new { label = l.Name, x.e.Amount })
                .GroupBy(x => x.label)
                .Select(g => new { label = g.Key, total = g.Sum(x => x.Amount) })
                .OrderByDescending(x => x.total)
                .ToListAsync(ct);

            return rows.Select(r => new { r.label, total = Math.Round((decimal)r.total, 2) }).ToList();
        }

        private async Task<object?> ExpenseBudgetVsActual(IDictionary<string, object?> a, CancellationToken ct)
        {
            // Arg: month_year (yyyy-MM-01)
            if (!a.TryGetValue("month_year", out var mv) || mv is null)
                throw new ArgumentException("Missing required arg 'month_year' (yyyy-MM-01)");

            mv = Unwrap(mv);
            DateOnly month;
            if (mv is DateOnly d) month = d;
            else if (mv is string s && DateOnly.TryParse(s, out var ds)) month = ds;
            else if (mv is DateTime dt) month = DateOnly.FromDateTime(dt);
            else throw new InvalidCastException("Arg 'month_year' must be ISO date (yyyy-MM-01)");

            var userId = GetGuidArg(a, "user_id");

            var start = new DateOnly(month.Year, month.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var actual = await _db.Expenses
                .Where(e => e.OccurredOn.HasValue
                    && e.OccurredOn.Value >= start
                    && e.OccurredOn.Value <= end
                    && (!userId.HasValue || e.UserId == userId))
                .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

            var budgetRow = await _db.Budgets
                .Where(b => b.MonthYear == start)
                .Select(b => new { b.MonthYear, b.MonthlyBudgetAmount })
                .FirstOrDefaultAsync(ct);

            decimal budget = budgetRow?.MonthlyBudgetAmount ?? 0m;
            var variance = actual - budget;
            decimal? variancePct = budget == 0 ? null : Math.Round(variance / budget * 100m, 2);

            return new
            {
                month_year = start,
                budget_amount = Math.Round(budget, 2),
                actual_amount = Math.Round(actual, 2),
                variance = Math.Round(variance, 2),
                variance_pct = variancePct
            };
        }

        private async Task<object?> ExpenseNotesSearch(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");
            if (!a.TryGetValue("query", out var qv) || qv is null)
                throw new ArgumentException("Missing required arg 'query'");
            var query = (Unwrap(qv)?.ToString() ?? "").Trim();
            var userId = GetGuidArg(a, "user_id");

            var rows = await _db.Expenses
                .Where(e => e.OccurredOn.HasValue
                    && e.OccurredOn.Value >= start
                    && e.OccurredOn.Value <= end
                    && (!userId.HasValue || e.UserId == userId)
                    && (e.Notes ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                .GroupJoin(_db.Categories, e => e.CategoryId, c => (Guid?)c.Id,
                    (e, cg) => new
                    {
                        e.OccurredOn,
                        e.Amount,
                        Category = cg.Select(x => x.Name).FirstOrDefault(),
                        e.ContactId,
                        e.Notes
                    })
                .GroupJoin(_db.Contacts, x => x.ContactId, co => (Guid?)co.Id,
                    (x, cog) => new
                    {
                        occurred_on = x.OccurredOn,
                        amount = x.Amount,
                        category = x.Category ?? "Uncategorized",
                        supplier = cog.Select(s => s.Name).FirstOrDefault() ?? "Unknown",
                        notes = x.Notes
                    })
                .OrderByDescending(r => r.occurred_on)
                .ToListAsync(ct);

            return rows.Select(r => new {
                r.occurred_on,
                amount = Math.Round((decimal)r.amount, 2),
                r.category,
                r.supplier,
                r.notes
            }).ToList();
        }

        //private async Task<object?> ExpenseBudgetVsActualCompat(IDictionary<string, object?> a, CancellationToken ct)
        //{
        //    try
        //    {
        //        // If month_year missing, derive from start (yyyy-MM-01)
        //        if (!a.ContainsKey("month_year") && a.TryGetValue("start", out var sv) && sv is not null)
        //        {
        //            var start = GetDateArg(a, "start");
        //            var first = new DateOnly(start.Year, start.Month, 1);
        //            a = new Dictionary<string, object?>(a, StringComparer.OrdinalIgnoreCase)
        //            {
        //                ["month_year"] = first.ToString("yyyy-MM-01")
        //            };
        //        }

        //        return await ExpenseBudgetVsActual(a, ct);
        //    }
        //    catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        //    {
        //        // budgets table not found → let the report render without budget section
        //        return null;
        //    }
        //}

        // -----------------------------
        // INVENTORY
        // -----------------------------
        private async Task<object?> InventorySnapshot(IDictionary<string, object?> a, CancellationToken ct)
        {
            // Args: as_of (optional; current schema doesn’t track historical snapshots — we return current stock)
            var rows = await _db.ProductCategories
                .Join(_db.Products,
                      pc => pc.ProductId,
                      p => p.ProductId,
                      (pc, p) => new
                      {
                          product_id = p.ProductId,
                          product = p.ProductName,
                          current_stock = pc.CurrentStock,
                          reorder_point = pc.ReorderPoint,
                          updated_stock = pc.UpdatedStock
                      })
                .OrderBy(r => r.product)
                .ToListAsync(ct);

            return rows;
        }

        private async Task<object?> LowStockItems(IDictionary<string, object?> a, CancellationToken ct)
        {
            // Args: threshold (optional), k (optional)
            var threshold = GetIntArg(a, "threshold", int.MinValue); // if not provided, use reorder_point rule
            var k = GetIntArg(a, "k", 10);

            var q = _db.ProductCategories
                .Join(_db.Products,
                      pc => pc.ProductId,
                      p => p.ProductId,
                      (pc, p) => new
                      {
                          product = p.ProductName,
                          current_stock = pc.CurrentStock,
                          reorder_point = pc.ReorderPoint
                      });

            if (threshold != int.MinValue)
                q = q.Where(x => x.current_stock <= threshold);
            else
                q = q.Where(x => x.current_stock <= x.reorder_point);

            var rows = await q
                .OrderBy(x => x.current_stock)
                .ThenBy(x => x.product)
                .Take(k)
                .ToListAsync(ct);

            return rows;
        }

        private async Task<object?> StockByCategory(IDictionary<string, object?> a, CancellationToken ct)
        {
            var raw = await _db.ProductCategories
                .Join(_db.Products,
                      pc => pc.ProductId,
                      p => p.ProductId,
                      (pc, p) => new { pc.CurrentStock, p.SupplierId })
                .Join(_db.Suppliers,
                      x => x.SupplierId,
                      s => s.SupplierId,
                      (x, s) => new { s.SupplierName, x.CurrentStock })
                .GroupBy(x => x.SupplierName)
                .Select(g => new { name = g.Key, amount = g.Sum(r => (decimal)r.CurrentStock) })
                .OrderByDescending(x => x.amount)
                .ToListAsync(ct);

            var total = raw.Sum(r => r.amount);
            return raw.Select(r => new {
                r.name,
                amount = r.amount,
                share_pct = total == 0 ? 0 : Math.Round((r.amount / total) * 100m, 2),
                note = ""
            }).ToList();
        }

        private Task<object?> StockMovementWeekly(IDictionary<string, object?> a, CancellationToken ct)
        {
            var start = GetDateArg(a, "start");
            var end = GetDateArg(a, "end");

            // Build a week-by-week zero series (placeholder until you add a movements table)
            var list = new List<object>();

            // Use Monday as ISO week start
            DateTime d = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            DateTime endDt = DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

            // align to Monday
            int deltaToMonday = ((int)DayOfWeek.Monday - (int)d.DayOfWeek + 7) % 7;
            d = d.AddDays(deltaToMonday);

            while (d <= endDt)
            {
                int isoWeek = ISOWeek.GetWeekOfYear(d);
                list.Add(new { week = isoWeek, incoming = 0, outgoing = 0 });
                d = d.AddDays(7);
            }

            // If range shorter than a week and list is empty, return one zero point
            if (list.Count == 0)
            {
                int isoWeek = ISOWeek.GetWeekOfYear(endDt);
                list.Add(new { week = isoWeek, incoming = 0, outgoing = 0 });
            }

            return Task.FromResult<object?>(list);
        }
        // Inventory Snapshot: high-level KPIs
        private async Task<object?> InvSnapshot(IDictionary<string, object?> a, CancellationToken ct)
        {
            // Sum over productcategory
            var q = from pc in _db.ProductCategories
                    select new
                    {
                        pc.CurrentStock,
                        pc.ReorderPoint
                    };

            var list = await q.ToListAsync(ct);

            var totalUnits = list.Sum(x => x.CurrentStock);
            var lowStock = list.Count(x => x.ReorderPoint > 0 && x.CurrentStock <= x.ReorderPoint);
            var outOfStock = list.Count(x => x.CurrentStock <= 0);

            // distinct SKUs = productcategory rows that represent sellable variants
            var totalSkus = await _db.ProductCategories.CountAsync(ct);

            return new
            {
                total_skus = totalSkus,
                total_units = totalUnits,
                low_stock = lowStock,
                out_of_stock = outOfStock
            };
        }

        // Inventory table by product (joins Product + ProductCategory)
        // QID: INV_BY_PRODUCT
        private async Task<object?> InvByProduct(IDictionary<string, object?> a, CancellationToken ct)
        {
            var limit = GetIntArg(a, "limit", 200);

            string? nameLike = null;
            if (a != null && a.TryGetValue("name_like", out var nv) && nv is not null)
            {
                var s = nv.ToString();
                if (!string.IsNullOrWhiteSpace(s) && s!.Length >= 2)
                    nameLike = s.Trim();
            }

            // snapshot by product (include zero stock if you want; here we include all)
            var q = _db.ProductCategories
                .Join(_db.Products, pc => pc.ProductId, p => p.ProductId,
                    (pc, p) => new
                    {
                        p.ProductId,
                        p.ProductName,
                        pc.ProductCategoryId,
                        pc.CurrentStock,
                        pc.ReorderPoint,
                        pc.Price,
                        pc.Cost,
                        pc.Color,
                        pc.AgeSize
                    })
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(nameLike))
            {
                var needle = nameLike.ToLower();
                q = q.Where(x => x.ProductName.ToLower().Contains(needle));
            }

            var rows = await q
                .OrderByDescending(x => x.CurrentStock)
                .ThenBy(x => x.ProductName)
                .Take(limit)
                .ToListAsync(ct);

            return rows.Select(x => new
            {
                product_id = x.ProductId,
                product_name = x.ProductName,
                product_category_id = x.ProductCategoryId,
                current_stock = x.CurrentStock,
                reorder_point = x.ReorderPoint,
                price = Math.Round(x.Price, 2),
                cost = Math.Round(x.Cost, 2),
                color = x.Color,
                agesize = x.AgeSize
            }).ToList();
        }

        // Low stock: current_stock <= reorder_point (reorder_point > 0)
        // QID: INV_LOW_STOCK
        private async Task<object?> InvLowStock(IDictionary<string, object?> a, CancellationToken ct)
        {
            var limit = GetIntArg(a, "limit", 100);

            string? nameLike = null;
            if (a != null && a.TryGetValue("name_like", out var nv) && nv is not null)
            {
                var s = nv.ToString();
                if (!string.IsNullOrWhiteSpace(s) && s!.Length >= 2)
                    nameLike = s.Trim();
            }

            // low = at/below reorder
            var q = _db.ProductCategories
                .Where(pc => pc.CurrentStock <= pc.ReorderPoint)
                .Join(_db.Products, pc => pc.ProductId, p => p.ProductId,
                    (pc, p) => new
                    {
                        p.ProductId,
                        p.ProductName,
                        pc.ProductCategoryId,
                        pc.CurrentStock,
                        pc.ReorderPoint
                    })
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(nameLike))
            {
                var needle = nameLike.ToLower();
                q = q.Where(x => x.ProductName.ToLower().Contains(needle));
            }

            // most urgent first: smallest (stock - reorder), then name
            var rows = await q
                .OrderBy(x => x.CurrentStock - x.ReorderPoint)
                .ThenBy(x => x.ProductName)
                .Take(limit)
                .ToListAsync(ct);

            return rows.Select(x => new
            {
                product_id = x.ProductId,
                product_name = x.ProductName,
                product_category_id = x.ProductCategoryId,
                current_stock = x.CurrentStock,
                reorder_point = x.ReorderPoint
            }).ToList();
        }

        // Out of stock: current_stock <= 0
        // QID: INV_OUT_OF_STOCK
        private async Task<object?> InvOutOfStock(IDictionary<string, object?> a, CancellationToken ct)
        {
            var limit = GetIntArg(a, "limit", 100);

            // optional: name contains filter (e.g., "out of stock unicorn")
            string? nameLike = null;
            if (a != null && a.TryGetValue("name_like", out var nv) && nv is not null)
            {
                var s = nv.ToString();
                if (!string.IsNullOrWhiteSpace(s) && s!.Length >= 2)
                    nameLike = s.Trim();
            }

            // OOS = strictly zero stock
            var q = _db.ProductCategories
                .Where(pc => pc.CurrentStock == 0)
                .Join(_db.Products, pc => pc.ProductId, p => p.ProductId,
                    (pc, p) => new
                    {
                        p.ProductId,
                        p.ProductName,
                        pc.ProductCategoryId,
                        pc.CurrentStock,
                        pc.ReorderPoint
                    })
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(nameLike))
            {
                var needle = nameLike.ToLower();
                q = q.Where(x => x.ProductName.ToLower().Contains(needle));
                // For PostgreSQL you can switch to:
                // q = q.Where(x => EF.Functions.ILike(x.ProductName, $"%{nameLike}%"));
            }

            // Sort by name for readability (you can change to ReorderPoint desc if you prefer)
            var rows = await q
                .OrderBy(x => x.ProductName)
                .Take(limit)
                .ToListAsync(ct);

            return rows.Select(x => new
            {
                product_id = x.ProductId,
                product_name = x.ProductName,
                product_category_id = x.ProductCategoryId,
                current_stock = x.CurrentStock,
                reorder_point = x.ReorderPoint
            }).ToList();
        }

        // Valuation at current cost (and optional price) across inventory
        private async Task<object?> InvValuationCurrent(IDictionary<string, object?> a, CancellationToken ct)
        {
            var rows = await _db.ProductCategories
                .Select(pc => new
                {
                    units = pc.CurrentStock,
                    pc.Cost,
                    pc.Price
                })
                .ToListAsync(ct);

            decimal valueCost = rows.Sum(r => (decimal)r.units * r.Cost);
            decimal valuePrice = rows.Sum(r => (decimal)r.units * r.Price);

            return new
            {
                value_at_cost = Math.Round(valueCost, 2),
                value_at_price = Math.Round(valuePrice, 2)
            };
        }
        // QID: INV_AVAILABLE_PRODUCTS
        private async Task<object?> InvAvailableProducts(IDictionary<string, object?> a, CancellationToken ct)
        {
            // read optional knobs (already supported upstream by your mapper)
            var limit = GetIntArg(a, "limit", 200);

            // optional: name contains filter (e.g., "available products unicorn")
            string? nameLike = null;
            if (a != null && a.TryGetValue("name_like", out var nv) && nv is not null)
            {
                var s = nv.ToString();
                if (!string.IsNullOrWhiteSpace(s) && s!.Length >= 2)
                    nameLike = s.Trim();
            }

            // base query: “available” = current stock > 0
            var q = _db.ProductCategories
                .Where(pc => pc.CurrentStock > 0)
                .Join(_db.Products, pc => pc.ProductId, p => p.ProductId,
                    (pc, p) => new
                    {
                        p.ProductId,
                        p.ProductName,
                        pc.ProductCategoryId,
                        pc.CurrentStock,
                        pc.ReorderPoint,
                        pc.Price,
                        pc.Cost,
                        pc.Color,
                        pc.AgeSize
                    })
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(nameLike))
            {
                var needle = nameLike.ToLower();
                q = q.Where(x => x.ProductName.ToLower().Contains(needle));
            }

            // most available first, then by name; cap to limit
            var rows = await q
                .OrderByDescending(x => x.CurrentStock)
                .ThenBy(x => x.ProductName)
                .Take(limit)
                .ToListAsync(ct);

            return rows.Select(x => new
            {
                product_id = x.ProductId,
                product_name = x.ProductName,
                product_category_id = x.ProductCategoryId,
                current_stock = x.CurrentStock,
                reorder_point = x.ReorderPoint,
                price = Math.Round(x.Price, 2),
                cost = Math.Round(x.Cost, 2),
                color = x.Color,
                agesize = x.AgeSize
            }).ToList();
        }

        // -----------------------------
        // SALES
        // -----------------------------
        private async Task<object?> SalesSummary(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var utcStart = ToDbStart(startD);
            var utcEnd = ToDbEnd(endD);

            var q = from oi in _db.OrderItems
                    where oi.Order != null
                       && oi.Order.OrderDate >= utcStart
                       && oi.Order.OrderDate <= utcEnd
                    select new { oi.Subtotal, oi.Quantity, oi.OrderId };

            var revenue = await q.SumAsync(x => (decimal?)x.Subtotal, ct) ?? 0m;
            var units = await q.SumAsync(x => (int?)x.Quantity, ct) ?? 0;
            var orders = await q.Select(x => x.OrderId).Distinct().CountAsync(ct);
            var aov = orders > 0 ? Math.Round(revenue / orders, 2) : 0m;

            // previous window: same span just before [start, end]
            var spanDays = (endD.ToDateTime(TimeOnly.MinValue) - startD.ToDateTime(TimeOnly.MinValue)).TotalDays + 1;
            var prevEndD = startD.AddDays(-1);
            var prevStartD = DateOnly.FromDateTime(prevEndD.ToDateTime(TimeOnly.MinValue).AddDays(-spanDays + 1));
            var prevStart = ToDbStart(prevStartD);
            var prevEnd = ToDbEnd(prevEndD);

            var qPrev = from oi in _db.OrderItems
                        where oi.Order != null
                           && oi.Order.OrderDate >= prevStart
                           && oi.Order.OrderDate <= prevEnd
                        select (decimal?)oi.Subtotal;

            var prevRevenue = await qPrev.SumAsync(ct) ?? 0m;
            var deltaPct = prevRevenue == 0 ? 0 : Math.Round(((revenue - prevRevenue) / prevRevenue) * 100m, 2);

            return new
            {
                revenue = Math.Round(revenue, 2),
                orders,
                units,
                avg_order_value = aov,
                prev_revenue = Math.Round(prevRevenue, 2),
                delta_pct = deltaPct
            };
        }

        private async Task<object?> TopProducts(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);
            var k = GetIntArg(a, "k", 10);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null
                          && oi.Order.OrderDate >= dbStart
                          && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => new { oi.ProductId, oi.Product!.ProductName })
                .Select(g => new
                {
                    product = g.Key.ProductName,
                    revenue = g.Sum(x => x.Subtotal),
                    units = g.Sum(x => x.Quantity)
                })
                .OrderByDescending(x => x.revenue)
                .Take(k)
                .ToListAsync(ct);

            return rows.Select(r => new { r.product, revenue = Math.Round(r.revenue, 2), r.units }).ToList();
        }

        private async Task<object?> SalesByDay(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null
                          && oi.Order.OrderDate >= dbStart
                          && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => oi.Order!.OrderDate.Date)
                .Select(g => new
                {
                    date = DateOnly.FromDateTime(g.Key),
                    revenue = g.Sum(x => x.Subtotal),
                    orders = g.Select(x => x.OrderId).Distinct().Count(),
                    units = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.date)
                .ToListAsync(ct);

            return rows.Select(r => new { r.date, revenue = Math.Round(r.revenue, 2), r.orders, r.units }).ToList();
        }
        private async Task<object?> SalesByWeek(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => ISOWeek.GetWeekOfYear(oi.Order!.OrderDate))
                .Select(g => new {
                    week = g.Key,
                    revenue = g.Sum(x => x.Subtotal),
                    orders = g.Select(x => x.OrderId).Distinct().Count(),
                    units = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.week)
                .ToListAsync(ct);

            return rows.Select(r => new { r.week, revenue = Math.Round(r.revenue, 2), r.orders, r.units }).ToList();
        }

        private async Task<object?> SalesByHour(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => new { d = oi.Order!.OrderDate.Date, h = oi.Order!.OrderDate.Hour })
                .Select(g => new {
                    date = DateOnly.FromDateTime(g.Key.d),
                    hour = g.Key.h,
                    revenue = g.Sum(x => x.Subtotal),
                    orders = g.Select(x => x.OrderId).Distinct().Count(),
                    units = g.Sum(x => x.Quantity)
                })
                .OrderBy(x => x.date).ThenBy(x => x.hour)
                .ToListAsync(ct);

            return rows.Select(r => new { r.date, r.hour, revenue = Math.Round(r.revenue, 2), r.orders, r.units }).ToList();
        }

        private async Task<object?> SalesByProduct(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => new { oi.ProductId, oi.Product!.ProductName })
                .Select(g => new {
                    product = g.Key.ProductName,
                    revenue = g.Sum(x => x.Subtotal),
                    units = g.Sum(x => x.Quantity),
                    orders = g.Select(x => x.OrderId).Distinct().Count()
                })
                .OrderByDescending(x => x.revenue)
                .ToListAsync(ct);

            return rows.Select(r => new { r.product, revenue = Math.Round(r.revenue, 2), r.units, r.orders }).ToList();
        }

        private async Task<object?> SalesByCategory(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            // "category" (default), "color", "agesize"
            var dim = (a.TryGetValue("dimension", out var dv) ? (Unwrap(dv)?.ToString() ?? "") : "").ToLowerInvariant();

            // Base join: OrderItems → Orders (for date) → ProductCategories (for attributes)
            var joined = _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .Join(_db.ProductCategories,
                      oi => oi.ProductCategoryId,
                      pc => pc.ProductCategoryId,
                      (oi, pc) => new { oi, pc });

            // Branch OUTSIDE the expression tree (avoid switch expressions in LINQ)
            if (dim == "color")
            {
                var q = await joined
                    .GroupBy(x => x.pc.Color)
                    .Select(g => new
                    {
                        name = g.Key ?? "(Unknown)",
                        revenue = g.Sum(y => y.oi.Subtotal),
                        units = g.Sum(y => y.oi.Quantity),
                        orders = g.Select(y => y.oi.OrderId).Distinct().Count()
                    })
                    .OrderByDescending(r => r.revenue)
                    .ToListAsync(ct);

                return q.Select(r => new
                {
                    name = r.name,
                    revenue = Math.Round(r.revenue, 2),
                    r.units,
                    r.orders
                }).ToList();
            }
            else if (dim == "agesize")
            {
                var q = await joined
                    .GroupBy(x => x.pc.AgeSize)
                    .Select(g => new
                    {
                        name = g.Key ?? "(Unknown)",
                        revenue = g.Sum(y => y.oi.Subtotal),
                        units = g.Sum(y => y.oi.Quantity),
                        orders = g.Select(y => y.oi.OrderId).Distinct().Count()
                    })
                    .OrderByDescending(r => r.revenue)
                    .ToListAsync(ct);

                return q.Select(r => new
                {
                    name = r.name,
                    revenue = Math.Round(r.revenue, 2),
                    r.units,
                    r.orders
                }).ToList();
            }
            else
            {
                // DEFAULT "category": use a composite of Color + AgeSize as a readable label
                // (If you have a proper category name field, swap this to that field.)
                var q = await joined
                    .GroupBy(x => new { x.pc.Color, x.pc.AgeSize })
                    .Select(g => new
                    {
                        name = (g.Key.Color ?? "") + (string.IsNullOrEmpty(g.Key.AgeSize) ? "" : " " + g.Key.AgeSize),
                        revenue = g.Sum(y => y.oi.Subtotal),
                        units = g.Sum(y => y.oi.Quantity),
                        orders = g.Select(y => y.oi.OrderId).Distinct().Count()
                    })
                    .OrderByDescending(r => r.revenue)
                    .ToListAsync(ct);

                return q.Select(r => new
                {
                    name = string.IsNullOrWhiteSpace(r.name) ? "(Unknown)" : r.name,
                    revenue = Math.Round(r.revenue, 2),
                    r.units,
                    r.orders
                }).ToList();
            }
        }

        private async Task<object?> BottomProducts(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);
            var k = GetIntArg(a, "k", 10);
            var sort = (a.TryGetValue("sort", out var sv) ? (Unwrap(sv)?.ToString() ?? "") : "").ToLowerInvariant(); // "revenue"|"units"

            var q = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => new { oi.ProductId, oi.Product!.ProductName })
                .Select(g => new {
                    product = g.Key.ProductName,
                    revenue = g.Sum(x => x.Subtotal),
                    units = g.Sum(x => x.Quantity)
                })
                .ToListAsync(ct);

            var ordered = sort == "units"
                ? q.OrderBy(x => x.units).ThenBy(x => x.product)
                : q.OrderBy(x => x.revenue).ThenBy(x => x.product);

            var rows = ordered.Take(k).Select(r => new { r.product, revenue = Math.Round(r.revenue, 2), r.units }).ToList();
            return rows;
        }

        private async Task<object?> OrdersByStatus(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.Orders
                .Where(o => o.OrderDate >= dbStart && o.OrderDate <= dbEnd)
                .GroupBy(o => o.OrderStatus ?? "(Unknown)")
                .Select(g => new { status = g.Key, orders = g.Count() })
                .OrderByDescending(x => x.orders)
                .ToListAsync(ct);

            return rows;
        }

        private async Task<object?> AovByDay(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => oi.Order!.OrderDate.Date)
                .Select(g => new {
                    date = DateOnly.FromDateTime(g.Key),
                    revenue = g.Sum(x => x.Subtotal),
                    orders = g.Select(x => x.OrderId).Distinct().Count()
                })
                .OrderBy(x => x.date)
                .ToListAsync(ct);

            return rows.Select(r => new { r.date, aov = r.orders > 0 ? Math.Round(r.revenue / r.orders, 2) : 0m }).ToList();
        }

        private async Task<object?> AovByWeek(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => ISOWeek.GetWeekOfYear(oi.Order!.OrderDate))
                .Select(g => new {
                    week = g.Key,
                    revenue = g.Sum(x => x.Subtotal),
                    orders = g.Select(x => x.OrderId).Distinct().Count()
                })
                .OrderBy(x => x.week)
                .ToListAsync(ct);

            return rows.Select(r => new { r.week, aov = r.orders > 0 ? Math.Round(r.revenue / r.orders, 2) : 0m }).ToList();
        }

        private async Task<object?> OrderSizeDistribution(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);

            // Compute units per order then bucketize
            var perOrder = await _db.OrderItems
                .Where(oi => oi.Order != null && oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => oi.OrderId)
                .Select(g => new { orderId = g.Key, units = g.Sum(x => x.Quantity) })
                .ToListAsync(ct);

            int b1 = 0, b2 = 0, b3 = 0, b4 = 0;
            foreach (var r in perOrder)
            {
                if (r.units <= 1) b1++;
                else if (r.units <= 3) b2++;
                else if (r.units <= 5) b3++;
                else b4++;
            }

            return new[]
            {
        new { bucket = "1", orders = b1 },
        new { bucket = "2–3", orders = b2 },
        new { bucket = "4–5", orders = b3 },
        new { bucket = "6+", orders = b4 }
    };
        }

        private async Task<object?> RecentOrders(IDictionary<string, object?> a, CancellationToken ct)
        {
            var limit = GetIntArg(a, "limit", 20);

            var rows = await _db.Orders
                .OrderByDescending(o => o.OrderDate)
                .ThenByDescending(o => o.OrderId)
                .Take(limit)
                .Select(o => new {
                    order_id = o.OrderId,
                    order_date = o.OrderDate,
                    order_status = o.OrderStatus,
                    amount_paid = o.AmountPaid,
                    change = o.Change
                })
                .ToListAsync(ct);

            return rows;
        }

        private async Task<object?> OrderDetail(IDictionary<string, object?> a, CancellationToken ct)
        {
            if (!a.TryGetValue("order_id", out var ov) || ov is null)
                throw new ArgumentException("Missing required arg 'order_id'");
            var orderId = Convert.ToInt32(Unwrap(ov), CultureInfo.InvariantCulture);

            var header = await _db.Orders
                .Where(o => o.OrderId == orderId)
                .Select(o => new {
                    order_id = o.OrderId,
                    order_date = o.OrderDate,
                    order_status = o.OrderStatus,
                    amount_paid = o.AmountPaid,
                    change = o.Change
                })
                .FirstOrDefaultAsync(ct);

            if (header is null) return null;

            var lines = await _db.OrderItems
                .Where(oi => oi.OrderId == orderId)
                .Select(oi => new {
                    product = oi.Product!.ProductName,
                    unit_price = oi.UnitPrice,
                    quantity = oi.Quantity,
                    subtotal = oi.Subtotal
                })
                .ToListAsync(ct);

            return new { header, lines };
        }
        private async Task<object?> SalesByProductDay(IDictionary<string, object?> a, CancellationToken ct)
        {
            var startD = GetDateArg(a, "start");
            var endD = GetDateArg(a, "end");
            var dbStart = ToDbStart(startD);
            var dbEnd = ToDbEnd(endD);
            var topK = GetIntArg(a, "top_k", 10);

            // topK products by revenue in window
            var top = await _db.OrderItems
                .Where(oi => oi.Order != null &&
                             oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd)
                .GroupBy(oi => new { oi.ProductId, oi.Product!.ProductName })
                .Select(g => new { g.Key.ProductId, name = g.Key.ProductName, revenue = g.Sum(x => x.Subtotal) })
                .OrderByDescending(x => x.revenue).Take(topK)
                .AsNoTracking()
                .ToListAsync(ct);

            var topIds = top.Select(x => x.ProductId).ToHashSet();

            var rows = await _db.OrderItems
                .Where(oi => oi.Order != null &&
                             oi.Order.OrderDate >= dbStart && oi.Order.OrderDate <= dbEnd &&
                             topIds.Contains(oi.ProductId))
                .GroupBy(oi => new { oi.ProductId, oi.Product!.ProductName, d = oi.Order!.OrderDate.Date })
                .Select(g => new {
                    product = g.Key.ProductName,
                    date = DateOnly.FromDateTime(g.Key.d),
                    units = g.Sum(x => x.Quantity),
                    revenue = g.Sum(x => x.Subtotal)
                })
                .OrderBy(x => x.date).ThenBy(x => x.product)
                .AsNoTracking()
                .ToListAsync(ct);

            return rows.Select(r => new { r.product, r.date, r.units, revenue = Math.Round(r.revenue, 2) }).ToList();
        }
    }
}
