namespace Ecommerce_Backend.Extensions
{
    public static class TelemetryNames
    {
        public const string ApiServiceName = "inventory-api";
        public const string ConsumerServiceName = "order-consumer";

        // Sources and meters the app creates itself; registered with the OpenTelemetry SDK.
        public const string ActivitySourceName = "Ecommerce_Backend";
        public const string MeterName = "Ecommerce_Backend";
    }
}
