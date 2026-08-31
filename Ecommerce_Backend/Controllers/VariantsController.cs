using Ecommerce_Backend.Models;
using Ecommerce_Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VariantsController : ControllerBase
    {
        private readonly IVariantService _variantService;

        public VariantsController(IVariantService variantService)
        {
            _variantService = variantService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllVariants([FromQuery] int? productId)
        {
            var variants = await _variantService.GetAllVariantsAsync(productId);
            return Ok(variants);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetVariantById(int id)
        {
            var dto = await _variantService.GetVariantByIdAsync(id);

            if (dto == null)
            {
                return NotFound();
            }

            return Ok(dto);
        }

        [HttpPost]
        public async Task<IActionResult> CreateVariant([FromBody] Variant variant)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                await _variantService.CreateVariantAsync(variant);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            return CreatedAtAction(nameof(GetVariantById), new { id = variant.Id }, variant);
        }
    }
}
