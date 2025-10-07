using System;

namespace dataAccess.Entities
{
    public class ProductCategory
    {
        public int ProductCategoryId { get; set; }
        public int ProductId { get; set; }
        public decimal Price { get; set; }
        public decimal Cost { get; set; }
        public string? Color { get; set; }
        public string? AgeSize { get; set; }
        public int CurrentStock { get; set; }
        public int ReorderPoint { get; set; }
        public DateTime? UpdatedStock { get; set; }
    }
}
