using Ecommerce_Backend.Models;
using Ecommerce_Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Ecommerce_Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;

        public ProductsController(IProductService productService)
        {
            _productService = productService;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProductById(int id)
        {
            var dto = await _productService.GetProductByIdAsync(id);

            if (dto == null)
            {
                return NotFound();
            }

            return Ok(dto);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] Product product)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Product created;
            try
            {
                created = await _productService.CreateProductAsync(product);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            var dto = await _productService.GetProductByIdAsync(created.Id);
            return CreatedAtAction(nameof(GetProductById), new { id = created.Id }, dto);
        }
    }
}
