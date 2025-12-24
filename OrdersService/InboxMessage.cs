namespace OrdersService.Persistence;

public sealed class InboxMessage
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }
    
    public DateTimeOffset ReceivedAtUtc { get; set; }
}