using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Models
{

        public class Product
        {
            public int Id { get; set; }

            [Required]
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

            [Range(0.01, double.MaxValue, ErrorMessage = "Base price must be greater than zero.")]
            public decimal BasePrice { get; set; }

            public string Material { get; set; } = string.Empty;

            [Required]
            public int CategoryId { get; set; }
            public Category? Category { get; set; }

            public List<Variant> Variants { get; set; } = [];

            public List<Collection> Collections { get; set; } = [];
        }
 }
