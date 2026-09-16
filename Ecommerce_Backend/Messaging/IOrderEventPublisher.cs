namespace Ecommerce_Backend.Messaging
{
    public interface IOrderEventPublisher
    {
        Task PublishOrderPlacedAsync(OrderPlacedMessage message, CancellationToken cancellationToken = default);
    }
}
