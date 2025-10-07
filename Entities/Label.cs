using System;
using System.ComponentModel.DataAnnotations;

namespace dataAccess.Entities
{
    public class Label
    {
        public Guid Id { get; set; }                    // was int LabelId
        public Guid? UserId { get; set; }               // was string?
        public string Name { get; set; } = string.Empty;
        public string? Color { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

}
