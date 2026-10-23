using System.Diagnostics;
using Ecommerce_Backend.Extensions;

namespace Ecommerce_Backend.Telemetry
{
    public static class AppTelemetry
    {
        public static readonly ActivitySource ActivitySource = new(TelemetryNames.ActivitySourceName);
    }
}
