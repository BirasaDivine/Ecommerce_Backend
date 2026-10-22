using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Configuration
{
    public class VersionOptions
    {
        public static readonly string Path = "Version";

        // The commit the image was built from (set by the Docker build); "unknown" for local runs.
        [Required(ErrorMessage = "Version:Commit must be set.")]
        public string Commit { get; set; } = "unknown";
    }
}
