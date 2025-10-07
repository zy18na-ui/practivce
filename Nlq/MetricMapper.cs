using System;
using System.Collections.Generic;
using System.Globalization;

namespace dataAccess.Planning.Nlq
{
    public sealed class MetricMapper
    {
        public (string queryId, Dictionary<string, object?> args, string metricKind) GetAnswerQuery(NlqResolvedPlan plan)
        {
            var start = plan.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = plan.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            switch ((plan.Domain ?? "").ToLowerInvariant())
            {
                case "expense":
                    {
                        var m = (plan.Metric ?? "total_expense").ToLowerInvariant();
                        return m switch
                        {
                            "total_expense" => ("EXPENSE_SUMMARY", new() { ["start"] = start, ["end"] = end }, "expense_summary"),
                            "daily" => ("EXPENSE_BY_DAY", new() { ["start"] = start, ["end"] = end }, "expense_daily"),
                            "by_category" => ("EXPENSE_BY_CATEGORY", new() { ["start"] = start, ["end"] = end }, "expense_by_category"),
                            "by_supplier" => ("EXPENSE_BY_SUPPLIER", new() { ["start"] = start, ["end"] = end }, "expense_by_supplier"),
                            "top_suppliers" => ("TOP_EXPENSE_SUPPLIERS", new() { ["start"] = start, ["end"] = end, ["k"] = 5 }, "expense_top_suppliers"),
                            "recent" => ("EXPENSE_RECENT_TRANSACTIONS", new() { ["limit"] = 20 }, "expense_recent"),
                            "label_breakdown" => ("EXPENSE_LABEL_BREAKDOWN", new() { ["start"] = start, ["end"] = end }, "expense_label_breakdown"),
                            "budget_vs_actual" => ("EXPENSE_BUDGET_VS_ACTUAL", new() { ["start"] = start, ["end"] = end }, "expense_budget_vs_actual"),
                            _ => ("EXPENSE_SUMMARY", new() { ["start"] = start, ["end"] = end }, "expense_summary")
                        };
                    }

                case "sales":
                    {
                        var m = (plan.Metric ?? "revenue").ToLowerInvariant();
                        return m switch
                        {
                            "revenue" => ("SALES_SUMMARY", new() { ["start"] = start, ["end"] = end }, "sales_revenue"),
                            "orders" => ("SALES_SUMMARY", new() { ["start"] = start, ["end"] = end }, "sales_orders"),
                            "units" => ("SALES_SUMMARY", new() { ["start"] = start, ["end"] = end }, "sales_units"),
                            "avg_order_value" => ("SALES_SUMMARY", new() { ["start"] = start, ["end"] = end }, "sales_aov"),
                            "top_product" => ("TOP_PRODUCTS", new() { ["start"] = start, ["end"] = end, ["k"] = 1 }, "sales_top_product"),
                            "top_products" => ("TOP_PRODUCTS", new() { ["start"] = start, ["end"] = end, ["k"] = 10 }, "sales_top_products"),
                            "bottom_products" => ("BOTTOM_PRODUCTS", new() { ["start"] = start, ["end"] = end, ["k"] = 10 }, "sales_bottom_products"),
                            "by_category" => ("SALES_BY_CATEGORY", new() { ["start"] = start, ["end"] = end }, "sales_by_category"),
                            "by_category_color" => ("SALES_BY_CATEGORY", new() { ["start"] = start, ["end"] = end, ["dimension"] = "color" }, "sales_by_category"),
                            "by_category_agesize" => ("SALES_BY_CATEGORY", new() { ["start"] = start, ["end"] = end, ["dimension"] = "agesize" }, "sales_by_category"),
                            "by_product" => ("SALES_BY_PRODUCT", new() { ["start"] = start, ["end"] = end }, "sales_by_product"),
                            "by_day" => ("SALES_BY_DAY", new() { ["start"] = start, ["end"] = end }, "sales_by_day"),
                            "by_week" => ("SALES_BY_WEEK", new() { ["start"] = start, ["end"] = end }, "sales_by_week"),
                            "by_hour" => ("SALES_BY_HOUR", new() { ["start"] = start, ["end"] = end }, "sales_by_hour"),
                            "orders_by_status" => ("ORDERS_BY_STATUS", new() { ["start"] = start, ["end"] = end }, "orders_by_status"),
                            "aov_by_day" => ("AOV_BY_DAY", new() { ["start"] = start, ["end"] = end }, "aov_by_day"),
                            "aov_by_week" => ("AOV_BY_WEEK", new() { ["start"] = start, ["end"] = end }, "aov_by_week"),
                            "order_size_dist" => ("ORDER_SIZE_DISTRIBUTION", new() { ["start"] = start, ["end"] = end }, "order_size_dist"),
                            "recent_orders" => ("RECENT_ORDERS", new() { ["limit"] = 20 }, "recent_orders"),
                            "order_detail" => ("ORDER_DETAIL", new() /* requires order_id via router/entity resolver */, "order_detail"),
                            _ => ("SALES_SUMMARY", new() { ["start"] = start, ["end"] = end }, "sales_revenue")
                        };
                    }


                case "inventory":
                    {
                        // Inventory queries are snapshot-ish; no time args needed in your current SqlCatalog.
                        var m = (plan.Metric ?? "snapshot").ToLowerInvariant();
                        return m switch
                        {
                            "summary" => ("INV_SNAPSHOT", new(), "inventory_snapshot"), // ✨ add this line
                            "snapshot" => ("INV_SNAPSHOT", new(), "inventory_snapshot"),
                            "available" => ("INV_AVAILABLE_PRODUCTS", new() { ["limit"] = 50 }, "inventory_available"),
                            "by_product" => ("INV_BY_PRODUCT", new() { ["limit"] = 100 }, "inventory_by_product"),
                            "low_stock" => ("INV_LOW_STOCK", new(), "inventory_low_stock"),
                            "out_of_stock" => ("INV_OUT_OF_STOCK", new(), "inventory_out_of_stock"),
                            "valuation" => ("INV_VALUATION_CURRENT", new(), "inventory_valuation"),
                            _ => ("INV_SNAPSHOT", new(), "inventory_snapshot")
                        };
                    }

                default:
                    throw new ArgumentOutOfRangeException(nameof(plan.Domain), "Unknown domain");
            }
        }

        public List<(string qid, Dictionary<string, object?> args)> GetReportQueries(NlqResolvedPlan plan)
        {
            var list = new List<(string, Dictionary<string, object?>)>();
            var start = plan.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = plan.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            switch ((plan.Domain ?? "").ToLowerInvariant())
            {
                case "expense":
                    list.Add(("EXPENSE_SUMMARY", new() { ["start"] = start, ["end"] = end }));
                    list.Add(("TOP_EXPENSE_CATEGORIES", new() { ["start"] = start, ["end"] = end, ["k"] = 5 }));
                    list.Add(("EXPENSE_BY_CATEGORY_WEEKLY", new() { ["start"] = start, ["end"] = end }));
                    break;

                case "sales":
                    list.Add(("SALES_SUMMARY", new() { ["start"] = start, ["end"] = end }));
                    list.Add(("TOP_PRODUCTS", new() { ["start"] = start, ["end"] = end, ["k"] = 10 }));
                    list.Add(("SALES_BY_DAY", new() { ["start"] = start, ["end"] = end }));
                    list.Add(("ORDERS_BY_STATUS", new() { ["start"] = start, ["end"] = end }));
                    break;


                case "inventory":
                    // Inventory report bundle using INV_* IDs
                    list.Add(("INV_SNAPSHOT", new()));
                    list.Add(("INV_VALUATION_CURRENT", new()));
                    list.Add(("INV_LOW_STOCK", new()));
                    list.Add(("INV_BY_PRODUCT", new() { ["limit"] = 100 }));
                    break;
            }
            return list;
        }
    }
}
