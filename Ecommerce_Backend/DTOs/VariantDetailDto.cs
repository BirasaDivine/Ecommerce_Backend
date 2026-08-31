namespace Ecommerce_Backend.DTOs
{
    public class VariantDetailDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string StockStatus { get; set; } = string.Empty;
        public bool Active { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
    }
}
