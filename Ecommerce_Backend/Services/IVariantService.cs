using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;

namespace Ecommerce_Backend.Services
{
    public interface IVariantService
    {
        Task<VariantDetailDto?> GetVariantByIdAsync(int id);
        Task<List<VariantDetailDto>> GetAllVariantsAsync(int? productId = null);
        Task<Variant> CreateVariantAsync(Variant variant);
    }
}
