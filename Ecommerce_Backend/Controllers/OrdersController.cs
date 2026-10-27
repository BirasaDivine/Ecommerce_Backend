using Ecommerce_Backend.Data;
using Ecommerce_Backend.DTOs;
using Ecommerce_Backend.Messaging;
using Ecommerce_Backend.Models;
using Ecommerce_Backend.Telemetry;
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
        private readonly OrderMetrics _metrics;

        public OrdersController(AppDbContext context, IOrderEventPublisher publisher, OrderMetrics metrics)
        {
            _context = context;
            _publisher = publisher;
            _metrics = metrics;
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

            _metrics.OrderPlaced();

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

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(int id)
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            // Another user's order is reported as missing, so its existence is not revealed.
            var order = await _context.Orders
                .Include(o => o.Variant)
                .FirstOrDefaultAsync(o => o.Id == id && o.ApplicationUserId == userId);

            if (order == null)
            {
                return NotFound();
            }

            const string notPending = "Only a Pending order can be cancelled.";
            if (order.Status != OrderStatus.Pending)
            {
                return Conflict(notPending);
            }

            order.Status = OrderStatus.Cancelled;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // The consumer resolved the order first.
                return Conflict(notPending);
            }

            _metrics.OrderCancelled(order.OrderDate);

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
