using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce_Backend.Controllers
{
    public class AddProductToCollectionRequest
    {
        public int ProductId { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CollectionsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CollectionsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult GetAllCollections()
        {
            var collections = _context.Collections
                .Select(c => new CollectionDto { Id = c.Id, Name = c.Name })
                .ToList();

            return Ok(collections);
        }

        [HttpGet("{id}")]
        public IActionResult GetCollectionById(int id)
        {
            var collection = _context.Collections
                .Include(c => c.Products)
                .FirstOrDefault(c => c.Id == id);

            if (collection == null)
            {
                return NotFound();
            }

            return Ok(ToDetailDto(collection));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public IActionResult CreateCollection([FromBody] Collection collection)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _context.Collections.Add(collection);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetCollectionById), new { id = collection.Id }, collection);
        }

        [HttpPost("{id}/products")]
        [Authorize(Roles = "Admin")]
        public IActionResult AddProductToCollection(int id, [FromBody] AddProductToCollectionRequest request)
        {
            var collection = _context.Collections
                .Include(c => c.Products)
                .FirstOrDefault(c => c.Id == id);

            if (collection == null)
            {
                return NotFound($"Collection {id} not found.");
            }

            var product = _context.Products.FirstOrDefault(p => p.Id == request.ProductId);

            if (product == null)
            {
                return NotFound($"Product {request.ProductId} not found.");
            }

            if (collection.Products.Any(p => p.Id == product.Id))
            {
                return BadRequest("Product is already in this collection.");
            }

            collection.Products.Add(product);
            _context.SaveChanges();

            return Ok(ToDetailDto(collection));
        }

        private static CollectionDetailDto ToDetailDto(Collection collection)
        {
            return new CollectionDetailDto
            {
                Id = collection.Id,
                Name = collection.Name,
                Products = collection.Products
                    .Select(p => new CollectionProductDto { Id = p.Id, Name = p.Name, BasePrice = p.BasePrice })
                    .ToList()
            };
        }
    }
}
