using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;

namespace Ecommerce_Backend.Services
{
    public interface ICategoryService
    {
        Task<CategoryDto?> GetCategoryByIdAsync(int id);
        Task<List<CategoryDto>> GetAllCategoriesAsync();
        Task<Category> CreateCategoryAsync(Category category);
        Task<Category?> UpdateCategoryAsync(int id, Category updated);
        Task<bool> DeleteCategoryAsync(int id);
    }
}
