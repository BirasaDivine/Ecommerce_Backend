using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public class DatabaseOptions
    {
        public static readonly string Path = "Database";

        [Required(ErrorMessage = "Database:ConnectionString must be set (e.g. via user secrets or the Database__ConnectionString environment variable).")]
        public string ConnectionString { get; set; } = string.Empty;

        public bool MigrateOnStartup { get; set; }
    }
}
