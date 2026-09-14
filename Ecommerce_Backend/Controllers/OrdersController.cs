using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Messaging;
using Ecommerce_Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ecommerce_Backend.Controllers
{
    public class BuyRequest
    {
        public int VariantId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
        public int Quantity { get; set; } = 1;
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IOrderEventPublisher _publisher;

        public OrdersController(AppDbContext context, IOrderEventPublisher publisher)
        {
            _context = context;
            _publisher = publisher;
        }

        [HttpPost]
        public async Task<IActionResult> Buy([FromBody] BuyRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var variant = await _context.Variants
                .Include(v => v.Product)
                .FirstOrDefaultAsync(v => v.Id == request.VariantId);

            if (variant == null)
            {
                return NotFound("Variant not found.");
            }

            if (!variant.Active)
            {
                return BadRequest("This variant is not available for purchase.");
            }

            // Stock is not checked or decremented here: that happens once, in the
            // background consumer, so the same-SKU race is resolved by Service Bus
            // session ordering instead of an in-request concurrency retry loop.
            var unitPrice = variant.Price ?? variant.Product!.BasePrice;

            var order = new Order
            {
                ApplicationUserId = userId,
                VariantId = variant.Id,
                Quantity = request.Quantity,
                UnitPrice = unitPrice,
                OrderDate = DateTime.UtcNow,
                Status = OrderStatus.Pending
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            await _publisher.PublishOrderPlacedAsync(new OrderPlacedMessage
            {
                OrderId = order.Id,
                VariantId = variant.Id,
                Sku = variant.Sku,
                Quantity = request.Quantity
            });

            return AcceptedAtAction(nameof(GetOrderById), new { id = order.Id }, ToDto(order, variant.Sku));
        }

        [HttpGet("{id}")]
        public IActionResult GetOrderById(int id)
        {
            var order = _context.Orders
                .Include(o => o.Variant)
                .FirstOrDefault(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            var userId = GetCurrentUserId();
            if (order.ApplicationUserId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return Ok(ToDto(order, order.Variant?.Sku ?? string.Empty));
        }

        private string? GetCurrentUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        }

        private static OrderDto ToDto(Order order, string sku)
        {
            return new OrderDto
            {
                Id = order.Id,
                VariantId = order.VariantId,
                Sku = sku,
                Quantity = order.Quantity,
                UnitPrice = order.UnitPrice,
                TotalPrice = order.UnitPrice * order.Quantity,
                OrderDate = order.OrderDate,
                Status = order.Status,
                RejectionReason = order.RejectionReason
            };
        }
    }
}
