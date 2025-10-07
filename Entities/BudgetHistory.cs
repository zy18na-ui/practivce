using System;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class BudgetHistory
    {
        public long BudgetHistoryId { get; set; }       // was int
        public int BudgetId { get; set; }
        public Budget Budget { get; set; } = default!;
        public decimal OldAmount { get; set; }
        public decimal NewAmount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
