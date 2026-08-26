using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }
        [HttpGet("{id}")]
        public IActionResult GetProductById(int id)
        {
            var product = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Variants)
                .FirstOrDefault(p => p.Id == id);

            if (product == null)
            {
                return NotFound();
            }

            var dto = new ProductDetailDto
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

            return Ok(dto);
        }
        [HttpPost]
        public IActionResult CreateProduct([FromBody] Product product)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                _context.Products.Add(product);
                _context.SaveChanges();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            return CreatedAtAction(nameof(GetProductById), new { id = product.Id }, product);
        }
    }
}