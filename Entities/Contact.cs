namespace dataAccess.Entities
{
    /// <summary>
    /// Lightweight contact/supplier lookup used by expenses.
    /// Mapped to table: public.contacts
    /// Columns: id (PK), name, phone, email, created_at, updated_at
    /// </summary>
    public class Contact
    {
        public Guid Id { get; set; }

        /// <summary>
        /// Display name of the contact/supplier/vendor.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional phone number.
        /// </summary>
        public string? Phone { get; set; }

        /// <summary>
        /// Optional email address.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// When the contact record was created (UTC or DB local, depending on your pipeline).
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// When the contact record was last updated.
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        public override string ToString() => $"{Name} ({Email ?? "no email"})";
    }
}
