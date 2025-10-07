using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dataAccess.Entities
{
    public class OrderItem
    {
        [Key]
        public int OrderItemId { get; set; }

        [ForeignKey(nameof(Order))]
        public int OrderId { get; set; }
        public Order? Order { get; set; }

        [ForeignKey(nameof(Product))]
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        // NEW: nullable FK per schema
        public int? ProductCategoryId { get; set; }  // productcategoryid

        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }       // unitprice
        public decimal Subtotal { get; set; }        // subtotal
        public DateTime CreatedAt { get; set; }      // createdat
        public DateTime UpdatedAt { get; set; }      // updatedat
    }
}
