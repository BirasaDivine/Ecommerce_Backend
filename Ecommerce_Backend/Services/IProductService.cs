using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;

namespace Ecommerce_Backend.Services
{
    public interface IProductService
    {
        Task<List<ProductDetailDto>> GetAllProductsAsync(string? name, decimal? maxPrice);
        Task<ProductDetailDto?> GetProductByIdAsync(int id);
        Task<Product> CreateProductAsync(Product product);
    }
}
