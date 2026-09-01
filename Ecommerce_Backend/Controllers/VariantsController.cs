using Ecommerce_Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Ecommerce_Backend.Controllers
{
    public class UpdateStockRequest
    {
        [Range(0, int.MaxValue, ErrorMessage = "Quantity cannot be negative.")]
        public int Quantity { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class VariantsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public VariantsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPatch("{sku}/stock")]
        [Authorize(Roles = "Admin")]
        public IActionResult UpdateStock(string sku, [FromBody] UpdateStockRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var variant = _context.Variants.FirstOrDefault(v => v.Sku == sku);

            if (variant == null)
            {
                return NotFound();
            }

            variant.Quantity = request.Quantity;
            _context.SaveChanges();

            return Ok(new { variant.Sku, variant.Quantity });
        }
    }
}
