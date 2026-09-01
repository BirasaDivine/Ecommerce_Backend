using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Models
{
    public class Order
    {
        public int Id { get; set; }

        [Required]
        public string ApplicationUserId { get; set; } = string.Empty;
        public ApplicationUser? ApplicationUser { get; set; }

        [Required]
        public int VariantId { get; set; }
        public Variant? Variant { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
        public int Quantity { get; set; }

        public decimal UnitPrice { get; set; }

        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    }
}
