namespace OrdersService.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public string Type { get; set; } = default!;
    public string PayloadJson { get; set; } = default!;
    public DateTimeOffset OccurredAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public string? LastError { get; set; }
}
