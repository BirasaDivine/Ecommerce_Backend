using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Services
{
    public class VariantService : IVariantService
    {
        private readonly AppDbContext _context;

        public VariantService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<VariantDetailDto?> GetVariantByIdAsync(int id)
        {
            var variant = await _context.Variants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == id);

            return variant == null ? null : ToDto(variant);
        }

        public async Task<List<VariantDetailDto>> GetAllVariantsAsync(int? productId = null)
        {
            var query = _context.Variants
                .Include(v => v.Product)
                .AsQueryable();

            if (productId.HasValue)
            {
                query = query.Where(v => v.ProductId == productId.Value);
            }

            var variants = await query.ToListAsync();
            return variants.Select(ToDto).ToList();
        }

        public async Task<Variant> CreateVariantAsync(Variant variant)
        {
            var productExists = await _context.Products.AnyAsync(p => p.Id == variant.ProductId);
            if (!productExists)
            {
                throw new InvalidOperationException(
                    $"Product with id '{variant.ProductId}' does not exist.");
            }

            _context.Variants.Add(variant);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                var duplicateExists = await _context.Variants.AnyAsync(v => v.Id != variant.Id && v.Sku == variant.Sku);
                if (duplicateExists)
                {
                    throw new InvalidOperationException($"A variant with SKU '{variant.Sku}' already exists.");
                }

                throw;
            }

            return variant;
        }

        public async Task<Variant?> UpdateStockAsync(string sku, int quantity)
        {
            var variant = await _context.Variants.FirstOrDefaultAsync(v => v.Sku == sku);
            if (variant == null)
            {
                return null;
            }

            variant.Quantity = quantity;
            await _context.SaveChangesAsync();
            return variant;
        }

        private static VariantDetailDto ToDto(Variant variant)
        {
            return new VariantDetailDto
            {
                Id = variant.Id,
                Name = variant.Name,
                Sku = variant.Sku,
                Price = variant.Price ?? variant.Product?.BasePrice ?? 0,
                Quantity = variant.Quantity,
                StockStatus = StockStatusHelper.GetStockStatus(variant.Quantity),
                Active = variant.Active,
                ProductId = variant.ProductId,
                ProductName = variant.Product?.Name ?? string.Empty
            };
        }
    }
}
