using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Models
{
    public class Variant
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        // Nullable: only set if this variant overrides the product's BasePrice
        public decimal? Price { get; set; }

        [Required]
        public string Sku { get; set; } = string.Empty;

        [Range(0, int.MaxValue, ErrorMessage = "Quantity cannot be negative.")]
        public int Quantity { get; set; }

        public bool Active { get; set; } = true;

        [Required]
        public int ProductId { get; set; }
        public Product? Product { get; set; }
    }
}
