using Ecommerce_Backend.Data;
using Ecommerce_Backend.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Ecommerce_Backend.Tests.Models
{
    [TestFixture]
    public class OrderConcurrencyTests
    {
        [Test]
        public void ResolvingAnOrderThatWasAlreadyResolved_ThrowsConcurrencyException()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            int orderId;
            using (var seed = new AppDbContext(options))
            {
                var order = new Order { ApplicationUserId = "user-1", VariantId = 1, Quantity = 1, Status = OrderStatus.Pending };
                seed.Orders.Add(order);
                seed.SaveChanges();
                orderId = order.Id;
            }

            // Both sides read the order while it is still Pending, as the API and the consumer can.
            using var consumerContext = new AppDbContext(options);
            using var apiContext = new AppDbContext(options);
            var consumerOrder = consumerContext.Orders.Single(o => o.Id == orderId);
            var apiOrder = apiContext.Orders.Single(o => o.Id == orderId);

            consumerOrder.Status = OrderStatus.Confirmed;
            consumerContext.SaveChanges();

            apiOrder.Status = OrderStatus.Cancelled;
            Assert.Throws<DbUpdateConcurrencyException>(() => apiContext.SaveChanges());

            using var verify = new AppDbContext(options);
            Assert.That(verify.Orders.Single(o => o.Id == orderId).Status, Is.EqualTo(OrderStatus.Confirmed));
        }
    }
}
