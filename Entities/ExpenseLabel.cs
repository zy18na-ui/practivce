namespace dataAccess.Entities
{
    public class ExpenseLabel
    {
        public Guid ExpenseId { get; set; }             // was int
        public Expense Expense { get; set; } = default!;
        public Guid LabelId { get; set; }               // was int
        public Label Label { get; set; } = default!;
    }
}
