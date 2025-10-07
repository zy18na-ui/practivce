using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        public DateTime OrderDate { get; set; }   // orderdate

        [Range(typeof(decimal), "0", "79228162514264337593543950335")]
        public decimal TotalAmount { get; set; }  // totalamount

        public string OrderStatus { get; set; } = string.Empty; // orderstatus
        public DateTime CreatedAt { get; set; }   // createdat
        public DateTime UpdatedAt { get; set; }   // updatedat

        // NEW: required by schema
        public decimal AmountPaid { get; set; }   // amount_paid
        public decimal Change { get; set; }       // change

        // Navigation
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    }
}
