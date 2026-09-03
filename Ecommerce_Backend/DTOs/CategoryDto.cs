namespace Ecommerce_Backend.DTOs
{
    public class CategoryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ParentCategoryId { get; set; }
        public string? ParentCategoryName { get; set; }
    }

    public class CategoryDetailDto : CategoryDto
    {
        public List<CategoryDto> Children { get; set; } = [];
    }
}
