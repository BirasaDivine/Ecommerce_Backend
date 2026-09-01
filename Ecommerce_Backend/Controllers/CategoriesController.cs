using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CategoriesController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAllCategories()
        {
            var categories = _context.Categories
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ParentCategoryId = c.ParentCategoryId
                })
                .ToList();

            return Ok(categories);
        }

        [HttpGet("{id}")]
        public IActionResult GetCategoryById(int id)
        {
            var category = _context.Categories.FirstOrDefault(c => c.Id == id);

            if (category == null)
            {
                return NotFound();
            }

            var children = _context.Categories
                .Where(c => c.ParentCategoryId == id)
                .Select(c => new CategoryDto { Id = c.Id, Name = c.Name, ParentCategoryId = c.ParentCategoryId })
                .ToList();

            var dto = new CategoryDetailDto
            {
                Id = category.Id,
                Name = category.Name,
                ParentCategoryId = category.ParentCategoryId,
                Children = children
            };

            return Ok(dto);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public IActionResult CreateCategory([FromBody] Category category)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (category.ParentCategoryId.HasValue &&
                !_context.Categories.Any(c => c.Id == category.ParentCategoryId.Value))
            {
                return BadRequest("Parent category not found.");
            }

            try
            {
                _context.Categories.Add(category);
                _context.SaveChanges();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            return CreatedAtAction(nameof(GetCategoryById), new { id = category.Id }, category);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult UpdateCategory(int id, [FromBody] Category updated)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var category = _context.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
            {
                return NotFound();
            }

            if (updated.ParentCategoryId == id)
            {
                return BadRequest("A category cannot be its own parent.");
            }

            if (updated.ParentCategoryId.HasValue)
            {
                if (!_context.Categories.Any(c => c.Id == updated.ParentCategoryId.Value))
                {
                    return BadRequest("Parent category not found.");
                }

                if (IsDescendant(updated.ParentCategoryId.Value, id))
                {
                    return BadRequest("Cannot assign a descendant category as the parent.");
                }
            }

            category.Name = updated.Name;
            category.ParentCategoryId = updated.ParentCategoryId;

            try
            {
                _context.SaveChanges();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            return Ok(category);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult DeleteCategory(int id)
        {
            var category = _context.Categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
            {
                return NotFound();
            }

            if (_context.Categories.Any(c => c.ParentCategoryId == id))
            {
                return BadRequest("Cannot delete a category that has subcategories.");
            }

            if (_context.Products.Any(p => p.CategoryId == id))
            {
                return BadRequest("Cannot delete a category that has products assigned to it.");
            }

            _context.Categories.Remove(category);
            _context.SaveChanges();

            return NoContent();
        }

        // Walks up the parent chain from candidateId; true if ancestorId appears in it
        // (used to reject moves that would turn the tree into a cycle).
        private bool IsDescendant(int candidateId, int ancestorId)
        {
            var current = _context.Categories.FirstOrDefault(c => c.Id == candidateId);
            while (current?.ParentCategoryId != null)
            {
                if (current.ParentCategoryId == ancestorId)
                {
                    return true;
                }
                current = _context.Categories.FirstOrDefault(c => c.Id == current.ParentCategoryId);
            }
            return false;
        }
    }
}
