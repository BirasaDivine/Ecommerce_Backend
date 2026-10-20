namespace Ecommerce_Backend.Configuration
{
    public class DatabaseOptions
    {
        public static readonly string Path = "Database";

        public bool MigrateOnStartup { get; set; }
    }
}
