namespace Ecommerce_Backend.Configuration
{
    public class DatabaseOptions
    {
        public static readonly string Path = "Database";

        // Off by default so migrations against a real database stay a deliberate step;
        // docker-compose turns it on so a fresh container database gets its schema.
        public bool MigrateOnStartup { get; set; }
    }
}
