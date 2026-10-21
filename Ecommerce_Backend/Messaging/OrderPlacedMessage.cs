namespace Ecommerce_Backend.Messaging
{
    public class OrderPlacedMessage
    {
        public int OrderId { get; set; }
        public int VariantId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
