namespace Ecommerce_Backend.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }
        public int VariantId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
        public DateTime OrderDate { get; set; }
    }
}
