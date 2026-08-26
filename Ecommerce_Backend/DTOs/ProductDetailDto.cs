namespace Ecommerce_Backend.DTOs
{
    public class ProductDetailDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public string Material { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public List<VariantDto> Variants { get; set; } = []; 
    }

    public class VariantDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string StockStatus { get; set; } = string.Empty;
    }

}
