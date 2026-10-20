using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public class JwtOptions
    {
        public static readonly string Path = "Jwt";

        [Required(ErrorMessage = "Jwt:Issuer must be set.")]
        public string Issuer { get; set; } = string.Empty;

        [Required(ErrorMessage = "Jwt:Audience must be set.")]
        public string Audience { get; set; } = string.Empty;

        [Required(ErrorMessage = "Jwt:Key must be set (e.g. via user secrets or the Jwt__Key environment variable).")]
        [MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters long.")]
        public string Key { get; set; } = string.Empty;

        [Range(1, 1440, ErrorMessage = "Jwt:ExpiryMinutes must be between 1 and 1440.")]
        public int ExpiryMinutes { get; set; } = 60;
    }
}
