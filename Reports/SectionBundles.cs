using System.Collections.Generic;

namespace dataAccess.Reports;

public static class SectionBundles
{
    public static class Sales
    {
        public const string Performance = "sales_performance";
        public const string BestSelling = "best_selling";
        public const string SaleTrends = "sale_trends";

        // ---- OVERALL (existing) ----
        public static IReadOnlyList<QuerySpec> PerformanceQueries(string start, string end, bool includePrior, string? prevStart, string? prevEnd)
        {
            var list = new List<QuerySpec> {
                new("SALES_SUMMARY", new() { ["start"]=start, ["end"]=end })
            };
            if (includePrior && prevStart != null && prevEnd != null)
                list.Add(new("SALES_SUMMARY", new() { ["start"] = prevStart, ["end"] = prevEnd, ["_role"] = "previous" }));
            return list;
        }

        public static IReadOnlyList<QuerySpec> BestSellingQueries(string start, string end, int k = 10)
            => new List<QuerySpec> { new("TOP_PRODUCTS", new() { ["start"] = start, ["end"] = end, ["k"] = k }) };

        public static IReadOnlyList<QuerySpec> TrendQueries(string start, string end)
            => new List<QuerySpec> { new("SALES_BY_DAY", new() { ["start"] = start, ["end"] = end }) };

        // ---- ITEM-SPECIFIC (new) ----
        public static IReadOnlyList<QuerySpec> ItemPerformanceQueries(string start, string end, string productId, bool includePrior, string? prevStart, string? prevEnd)
        {
            var list = new List<QuerySpec> {
                new("ITEM_SUMMARY", new() { ["start"]=start, ["end"]=end, ["product_id"]=productId })
            };
            if (includePrior && prevStart != null && prevEnd != null)
                list.Add(new("ITEM_SUMMARY", new() { ["start"] = prevStart, ["end"] = prevEnd, ["product_id"] = productId, ["_role"] = "previous" }));
            return list;
        }

        public static IReadOnlyList<QuerySpec> ItemVariantsQueries(string start, string end, string productId, int k = 10)
            => new List<QuerySpec> { new("ITEM_VARIANTS_TOP", new() { ["start"] = start, ["end"] = end, ["product_id"] = productId, ["k"] = k }) };

        public static IReadOnlyList<QuerySpec> ItemTrendQueries(string start, string end, string productId)
            => new List<QuerySpec> { new("ITEM_TRENDS_DAILY", new() { ["start"] = start, ["end"] = end, ["product_id"] = productId }) };
    }

    public static class Expense
    {
        public const string Overview = "spending_overview";
        public const string TopCategories = "top_categories";
        public const string Anomalies = "anomalies_spikes";

        public static IReadOnlyList<QuerySpec> OverviewQueries(string start, string end, bool includePrior, string? prevStart, string? prevEnd, string? ym)
        {
            var list = new List<QuerySpec> {
                new("EXPENSE_SUMMARY", new() { ["start"]=start, ["end"]=end })
            };
            if (includePrior && prevStart != null && prevEnd != null)
                list.Add(new("EXPENSE_SUMMARY", new() { ["start"] = prevStart, ["end"] = prevEnd, ["_role"] = "previous" }));
            if (ym is not null)
                list.Add(new("BUDGET_UTILIZATION", new() { ["month_year"] = ym }));
            return list;
        }

        public static IReadOnlyList<QuerySpec> TopCategoryQueries(string start, string end, int k = 10)
            => new List<QuerySpec> { new("TOP_EXPENSE_CATEGORIES", new() { ["start"] = start, ["end"] = end, ["k"] = k }) };

        public static IReadOnlyList<QuerySpec> AnomalyQueries(string start, string end)
            => new List<QuerySpec> { new("EXPENSE_BY_DAY", new() { ["start"] = start, ["end"] = end }) };
    }

    public static class Inventory
    {
        public const string Snapshot = "availability_snapshot";
        public const string SlowMovers = "slow_movers";
        public const string Demand = "demand_trend";

        public static IReadOnlyList<QuerySpec> SnapshotQueries(string asOf, int lowStockThreshold = 5)
            => new List<QuerySpec> {
                new("INV_AVAILABLE_PRODUCTS", new() { ["as_of"]=asOf, ["limit"]=200 }),
                new("INV_LOW_STOCK",         new() { ["as_of"]=asOf, ["threshold"]=lowStockThreshold }),
                new("INV_OUT_OF_STOCK",      new() { ["as_of"]=asOf })
            };

        public static IReadOnlyList<QuerySpec> SlowMoverQueries(string start, string end, int limit = 20)
            => new List<QuerySpec> {
                new("INV_BY_PRODUCT", new() { ["start"]=start, ["end"]=end, ["limit"]=limit, ["_purpose"]="slow_mover_calc" })
            };

        public static IReadOnlyList<QuerySpec> DemandTrendQueries(string start, string end, int topK = 10)
            => new List<QuerySpec> {
                new("SALES_BY_PRODUCT_DAY", new() { ["start"]=start, ["end"]=end, ["top_k"]=topK })
            };
    }

    public sealed record QuerySpec(string QueryId, Dictionary<string, object> Args);
}
