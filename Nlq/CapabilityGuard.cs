using System;

namespace dataAccess.Planning.Nlq;

public sealed class CapabilityGuard
{
    public (bool allowed, string? message) Check(NlqResolvedPlan plan)
    {
        // Inventory comparisons are not supported
        if (string.Equals(plan.Domain, "inventory", StringComparison.OrdinalIgnoreCase) && plan.CompareToPrior)
        {
            return (false,
                "Inventory comparisons aren’t supported in this MVP. " +
                "Try an **inventory summary** as of a date, or compare **sales** or **expense** instead.");
        }

        // Example guard: expense-by-product not supported in schema (suggest sales)
        var text = plan.Metric ?? "";
        if (string.Equals(plan.Domain, "expense", StringComparison.OrdinalIgnoreCase)
            && text.Contains("product", StringComparison.OrdinalIgnoreCase))
        {
            return (false,
                "Expense by product isn’t available. You can ask for **sales by product** or **total expenses** instead.");
        }

        return (true, null);
    }
}
