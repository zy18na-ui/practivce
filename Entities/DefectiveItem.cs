using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace dataAccess.Entities
{
    public class DefectiveItem
    {
        [Key]
        public int DefectiveItemId { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        public DateTime ReportedDate { get; set; }

        [MaxLength(250)]
        public string DefectDescription { get; set; } = string.Empty;

        public int Quantity { get; set; }

        [MaxLength(50)]
        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public int? ReportedByUserId { get; set; }
    }
}
