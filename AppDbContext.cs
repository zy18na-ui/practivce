using Microsoft.EntityFrameworkCore;
using dataAccess.Entities;

namespace dataAccess.Services
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        // --- DbSets ---
        public DbSet<Supplier> Suppliers { get; set; } = default!;
        public DbSet<ProductCategory> ProductCategories { get; set; } = default!;
        public DbSet<Product> Products { get; set; } = default!;
        public DbSet<Order> Orders { get; set; } = default!;
        public DbSet<OrderItem> OrderItems { get; set; } = default!;
        public DbSet<DefectiveItem> DefectiveItems { get; set; } = default!;

        // EXPENSE DOMAIN
        public DbSet<Expense> Expenses { get; set; } = default!;
        public DbSet<Category> Categories { get; set; } = default!;
        public DbSet<Contact> Contacts { get; set; } = default!;          // <— ADDED
        public DbSet<Label> Labels { get; set; } = default!;
        public DbSet<ExpenseLabel> ExpenseLabels { get; set; } = default!;
        public DbSet<Budget> Budgets { get; set; } = default!;
        public DbSet<BudgetHistory> BudgetHistories { get; set; } = default!;

        // Read-only projection for future Sales reporting
        public DbSet<Sales> Sales { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- Default schema (quoted lower-case names in Postgres) ---
            modelBuilder.HasDefaultSchema("public");

            // =========================
            // SALES PROJECTION (VIEW)
            // =========================
            modelBuilder.Entity<Sales>().ToView("sales");
            modelBuilder.Entity<Sales>().HasNoKey();

            // =========================
            // SUPPLIERS
            // =========================
            modelBuilder.Entity<Supplier>(e =>
            {
                e.ToTable("suppliers");
                e.HasKey(x => x.SupplierId);

                e.Property(x => x.SupplierId).HasColumnName("supplierid");
                e.Property(x => x.SupplierName).HasColumnName("suppliername");
                e.Property(x => x.ContactPerson).HasColumnName("contactperson");
                e.Property(x => x.PhoneNumber).HasColumnName("phonenumber");
                e.Property(x => x.SupplierEmail).HasColumnName("supplieremail");
                e.Property(x => x.Address).HasColumnName("address");
                e.Property(x => x.CreatedAt).HasColumnName("createdat");
                e.Property(x => x.UpdatedAt).HasColumnName("updatedat");
                e.Property(x => x.SupplierStatus).HasColumnName("supplierstatus");
                e.Property(x => x.DefectReturned).HasColumnName("defectreturned").IsRequired(false);
            });

            // =========================
            // PRODUCTS
            // =========================
            modelBuilder.Entity<Product>(e =>
            {
                e.ToTable("products");
                e.HasKey(p => p.ProductId);

                e.Property(p => p.ProductId).HasColumnName("productid");
                e.Property(p => p.ProductName).HasColumnName("productname");
                e.Property(p => p.Description).HasColumnName("description");
                e.Property(p => p.SupplierId).HasColumnName("supplierid");
                e.Property(p => p.CreatedAt).HasColumnName("createdat");
                e.Property(p => p.UpdatedAt).HasColumnName("updatedat");
                e.Property(p => p.ImageUrl).HasColumnName("image_url");
                e.Property(p => p.UpdatedByUserId).HasColumnName("updatedbyuserid");

                e.HasMany(p => p.OrderItems)
                    .WithOne(oi => oi.Product)
                    .HasForeignKey(oi => oi.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasMany(p => p.DefectiveItems)
                    .WithOne(di => di.Product)
                    .HasForeignKey(di => di.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =========================
            // PRODUCT CATEGORY / INVENTORY
            // =========================
            modelBuilder.Entity<ProductCategory>(e =>
            {
                e.ToTable("productcategory");
                e.HasKey(c => c.ProductCategoryId);

                e.Property(c => c.ProductCategoryId).HasColumnName("productcategoryid");
                e.Property(c => c.ProductId).HasColumnName("productid");
                e.Property(c => c.Price).HasColumnName("price");
                e.Property(c => c.Cost).HasColumnName("cost");
                e.Property(c => c.Color).HasColumnName("color").IsRequired(false);
                e.Property(c => c.AgeSize).HasColumnName("agesize").IsRequired(false);
                e.Property(c => c.CurrentStock).HasColumnName("currentstock");
                e.Property(c => c.ReorderPoint).HasColumnName("reorderpoint");
                e.Property(c => c.UpdatedStock).HasColumnName("updatedstock");
            });

            // =========================
            // ORDERS
            // =========================
            modelBuilder.Entity<Order>(b =>
            {
                b.ToTable("orders");
                b.HasKey(x => x.OrderId);

                b.Property(x => x.OrderId).HasColumnName("orderid");
                b.Property(x => x.OrderDate).HasColumnName("orderdate").HasColumnType("timestamp without time zone");
                b.Property(x => x.TotalAmount).HasColumnName("totalamount").HasPrecision(18, 2);
                b.Property(x => x.OrderStatus).HasColumnName("orderstatus");
                b.Property(x => x.CreatedAt).HasColumnName("createdat").HasColumnType("timestamp without time zone");
                b.Property(x => x.UpdatedAt).HasColumnName("updatedat").HasColumnType("timestamp without time zone");
                b.Property(x => x.AmountPaid).HasColumnName("amount_paid");
                b.Property(x => x.Change).HasColumnName("change");

                b.HasMany(o => o.OrderItems)
                    .WithOne(oi => oi.Order)
                    .HasForeignKey(oi => oi.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(b =>
            {
                b.ToTable("orderitems"); // unified (not "order_items")
                b.HasKey(x => x.OrderItemId);

                b.Property(x => x.OrderItemId).HasColumnName("orderitemid");
                b.Property(x => x.OrderId).HasColumnName("orderid");
                b.Property(x => x.ProductId).HasColumnName("productid");
                b.Property(x => x.ProductCategoryId).HasColumnName("productcategoryid");
                b.Property(x => x.Quantity).HasColumnName("quantity");
                b.Property(x => x.UnitPrice).HasColumnName("unitprice").HasPrecision(18, 2);
                b.Property(x => x.Subtotal).HasColumnName("subtotal").HasPrecision(18, 2);
                b.Property(x => x.CreatedAt).HasColumnName("createdat").HasColumnType("timestamp without time zone");
                b.Property(x => x.UpdatedAt).HasColumnName("updatedat").HasColumnType("timestamp without time zone");
            });

            modelBuilder.Entity<DefectiveItem>(b =>
            {
                b.ToTable("defective_items");
                b.HasKey(x => x.DefectiveItemId);

                b.Property(x => x.DefectiveItemId).HasColumnName("defectiveitemid");
                b.Property(x => x.ProductId).HasColumnName("productid");
                b.Property(x => x.Quantity).HasColumnName("quantity");
                b.Property(x => x.DefectDescription).HasColumnName("reason");
                b.Property(x => x.CreatedAt).HasColumnName("createdat").HasColumnType("timestamp without time zone");
                b.Property(x => x.UpdatedAt).HasColumnName("updatedat").HasColumnType("timestamp without time zone");
            });

            // =========================
            // EXPENSE DOMAIN
            // =========================
            // Category
            modelBuilder.Entity<Category>(e =>
            {
                e.ToTable("categories");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.IsActive).HasColumnName("is_active");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            });

            // Contact / Supplier lookups for expenses
            modelBuilder.Entity<Contact>(e =>
            {
                e.ToTable("contacts");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.Phone).HasColumnName("phone");
                e.Property(x => x.Email).HasColumnName("email");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            });

            // Label
            modelBuilder.Entity<Label>(e =>
            {
                e.ToTable("labels");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.Name).HasColumnName("name").IsRequired();
                e.Property(x => x.Color).HasColumnName("color");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");
            });

            // Expense
            modelBuilder.Entity<Expense>(e =>
            {
                e.ToTable("expenses");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).HasColumnName("id");
                e.Property(x => x.UserId).HasColumnName("user_id");
                e.Property(x => x.OccurredOn).HasColumnName("occurred_on"); // DateOnly (ensure Npgsql config)
                e.Property(x => x.CategoryId).HasColumnName("category_id");
                e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
                e.Property(x => x.Notes).HasColumnName("notes");
                e.Property(x => x.Status).HasColumnName("status");
                e.Property(x => x.ContactId).HasColumnName("contact_id");
                e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
                e.Property(x => x.CreatedAt).HasColumnName("created_at");

                e.HasOne(x => x.CategoryRef)
                    .WithMany()
                    .HasForeignKey(x => x.CategoryId)
                    .HasConstraintName("expenses_category_id_fkey");

                e.HasOne(x => x.ContactRef)
                    .WithMany()
                    .HasForeignKey(x => x.ContactId)
                    .HasConstraintName("expenses_contact_id_fkey");
            });

            // ExpenseLabel (join: Expense ↔ Label)
            modelBuilder.Entity<ExpenseLabel>(e =>
            {
                e.ToTable("expense_labels");
                e.HasKey(x => new { x.LabelId, x.ExpenseId }); // composite PK

                e.Property(x => x.ExpenseId).HasColumnName("expense_id");
                e.Property(x => x.LabelId).HasColumnName("label_id");

                e.HasOne(x => x.Expense)
                    .WithMany()
                    .HasForeignKey(x => x.ExpenseId)
                    .HasConstraintName("expense_labels_expense_id_fkey");

                e.HasOne(x => x.Label)
                    .WithMany()
                    .HasForeignKey(x => x.LabelId)
                    .HasConstraintName("expense_labels_label_id_fkey");
            });

            // Budget
            modelBuilder.Entity<Budget>(e =>
            {
                e.ToTable("budgets");
                e.HasKey(x => x.BudgetId);

                e.Property(x => x.BudgetId).HasColumnName("budgetid");
                e.Property(x => x.MonthYear).HasColumnName("month_year"); // date (YYYY-MM-01)
                e.Property(x => x.MonthlyBudgetAmount).HasColumnName("monthly_budget_amount").HasPrecision(18, 2);
                e.Property(x => x.CreatedAt).HasColumnName("createdat");

                e.HasIndex(x => x.MonthYear);
            });

            // BudgetHistory
            modelBuilder.Entity<BudgetHistory>(e =>
            {
                e.ToTable("budgethistory"); // keep as-is; adjust if DB uses "budget_history"
                e.HasKey(x => x.BudgetHistoryId);

                e.Property(x => x.BudgetHistoryId).HasColumnName("budgethistoryid");
                e.Property(x => x.BudgetId).HasColumnName("budgetid");
                e.Property(x => x.OldAmount).HasColumnName("old_amount").HasPrecision(18, 2);
                e.Property(x => x.NewAmount).HasColumnName("new_amount").HasPrecision(18, 2);
                e.Property(x => x.CreatedAt).HasColumnName("createdat");

                e.HasOne(x => x.Budget)
                    .WithMany()
                    .HasForeignKey(x => x.BudgetId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasIndex(x => x.BudgetId);
            });

            // ===== Tips =====
            // If you find yourself mapping many snake_case columns,
            // you can also enable:
            //    optionsBuilder.UseNpgsql(conn).UseSnakeCaseNamingConvention();
            // during DbContext registration instead of manual HasColumnName for each property.
        }
    }
}
