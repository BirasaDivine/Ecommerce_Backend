using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly AppDbContext _context;

        public CategoryService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<CategoryDto?> GetCategoryByIdAsync(int id)
        {
            var category = await _context.Categories
                .Include(c => c.ParentCategory)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                return null;
            }

            var children = await _context.Categories
                .Where(c => c.ParentCategoryId == id)
                .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, ParentCategoryId = c.ParentCategoryId })
                .ToListAsync();

            var dto = ToDto(category);
            return new CategoryDetailDto
            {
                Id = dto.Id,
                Name = dto.Name,
                ParentCategoryId = dto.ParentCategoryId,
                ParentCategoryName = dto.ParentCategoryName,
                Children = children
            };
        }

        public async Task<List<CategoryDto>> GetAllCategoriesAsync()
        {
            var categories = await _context.Categories
                .Include(c => c.ParentCategory)
                .ToListAsync();

            return categories.Select(ToDto).ToList();
        }

        public async Task<Category> CreateCategoryAsync(Category category)
        {
            if (category.ParentCategoryId.HasValue &&
                !await _context.Categories.AnyAsync(c => c.Id == category.ParentCategoryId.Value))
            {
                throw new InvalidOperationException("Parent category not found.");
            }

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();
            return category;
        }

        public async Task<Category?> UpdateCategoryAsync(int id, Category updated)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return null;
            }

            if (updated.ParentCategoryId == id)
            {
                throw new InvalidOperationException("A category cannot be its own parent.");
            }

            if (updated.ParentCategoryId.HasValue)
            {
                if (!await _context.Categories.AnyAsync(c => c.Id == updated.ParentCategoryId.Value))
                {
                    throw new InvalidOperationException("Parent category not found.");
                }

                if (await IsDescendantAsync(updated.ParentCategoryId.Value, id))
                {
                    throw new InvalidOperationException("Cannot assign a descendant category as the parent.");
                }
            }

            category.Name = updated.Name;
            category.ParentCategoryId = updated.ParentCategoryId;

            await _context.SaveChangesAsync();
            return category;
        }

        public async Task<bool> DeleteCategoryAsync(int id)
        {
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category == null)
            {
                return false;
            }

            if (await _context.Categories.AnyAsync(c => c.ParentCategoryId == id))
            {
                throw new InvalidOperationException("Cannot delete a category that has subcategories.");
            }

            if (await _context.Products.AnyAsync(p => p.CategoryId == id))
            {
                throw new InvalidOperationException("Cannot delete a category that has products assigned to it.");
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();
            return true;
        }

        // Walks up the parent chain from candidateId; true if ancestorId appears in it
        // (used to reject moves that would turn the tree into a cycle).
        private async Task<bool> IsDescendantAsync(int candidateId, int ancestorId)
        {
            var current = await _context.Categories.FirstOrDefaultAsync(c => c.Id == candidateId);
            while (current?.ParentCategoryId != null)
            {
                if (current.ParentCategoryId == ancestorId)
                {
                    return true;
                }
                current = await _context.Categories.FirstOrDefaultAsync(c => c.Id == current.ParentCategoryId);
            }
            return false;
        }

        private static CategoryDto ToDto(Category category)
        {
            return new CategoryDto
            {
                Id = category.Id,
                Name = category.Name,
                ParentCategoryId = category.ParentCategoryId,
                ParentCategoryName = category.ParentCategory?.Name
            };
        }
    }
}
