using System;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Budget
    {
        [Key]
        public int BudgetId { get; set; }

        // Represent month as the first day of that month (e.g., 2025-05-01)
        public DateOnly MonthYear { get; set; }

        // e.g., monthly budget amount
        public decimal MonthlyBudgetAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
