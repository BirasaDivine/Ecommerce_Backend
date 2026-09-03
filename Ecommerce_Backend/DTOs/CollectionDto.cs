namespace Ecommerce_Backend.DTOs
{
    public class CollectionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class CollectionDetailDto : CollectionDto
    {
        public List<CollectionProductDto> Products { get; set; } = [];
    }

    public class CollectionProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
    }
}
