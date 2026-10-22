using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public class SeedAdminOptions
    {
        public static readonly string Path = "SeedAdmin";

        [Required]
        [EmailAddress(ErrorMessage = "SeedAdmin:Email must be a valid email address.")]
        public string Email { get; set; } = "admin@ecommerce.local";

        [Required(ErrorMessage = "SeedAdmin:Password must be set.")]
        public string Password { get; set; } = "Admin123!";
    }
}
