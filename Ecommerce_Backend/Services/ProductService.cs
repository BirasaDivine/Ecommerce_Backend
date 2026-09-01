using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Services
{
    public class ProductService : IProductService
    {
        private readonly AppDbContext _context;

        public ProductService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ProductDetailDto>> GetAllProductsAsync(string? name, decimal? maxPrice)
        {
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Variants)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(name))
            {
                var search = name.ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(search));
            }

            if (maxPrice.HasValue)
            {
                query = query.Where(p => p.BasePrice <= maxPrice.Value);
            }

            var products = await query.ToListAsync();
            return products.Select(ToDto).ToList();
        }

        public async Task<ProductDetailDto?> GetProductByIdAsync(int id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Variants)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null)
            {
                return null;
            }

            return ToDto(product);
        }

        private static ProductDetailDto ToDto(Product product)
        {
            return new ProductDetailDto
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                BasePrice = product.BasePrice,
                Material = product.Material,
                CategoryName = product.Category?.Name ?? string.Empty,
                Variants = product.Variants
                    .Where(v => v.Active)
                    .Select(v => new VariantDto
                    {
                        Id = v.Id,
                        Name = v.Name,
                        Price = v.Price ?? product.BasePrice,
                        StockStatus = StockStatusHelper.GetStockStatus(v.Quantity)
                    })
                    .ToList()
            };
        }

        public async Task<Product> CreateProductAsync(Product product)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == product.CategoryId))
            {
                throw new InvalidOperationException("Category not found.");
            }

            if (await _context.Categories.AnyAsync(c => c.ParentCategoryId == product.CategoryId))
            {
                throw new InvalidOperationException(
                    "Products must be assigned to a terminal (leaf) category, not a category that has subcategories.");
            }

            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            return product;
        }
    }
}
