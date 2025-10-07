namespace dataAccess.Entities
{
    public class Expense
    {
        public Guid Id { get; set; }

        // Scope to the authenticated user/tenant
        public Guid? UserId { get; set; }

        // Date of the expense (use DateOnly? if your Npgsql/EF setup supports it)
        public DateOnly? OccurredOn { get; set; }

        // Foreign keys
        public Guid? CategoryId { get; set; }
        public Guid? ContactId { get; set; }

        // Core fields
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
        public string? Status { get; set; }

        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // ---- Navigation properties expected by AppDbContext ----
        public Category? CategoryRef { get; set; }
        public Contact? ContactRef { get; set; }
    }
}
