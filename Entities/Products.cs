using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        [Required, MaxLength(150)]
        public string ProductName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SupplierId { get; set; }
        public DateTime CreatedAt { get; set; }   // createdat
        public DateTime UpdatedAt { get; set; }   // updatedat
        public string? ImageUrl { get; set; }     // image_url

        // FIX: uuid in DB → Guid?
        public Guid? UpdatedByUserId { get; set; } // updatedbyuserid (uuid)

        // Navigation
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public ICollection<DefectiveItem>? DefectiveItems { get; set; }
    }
}
