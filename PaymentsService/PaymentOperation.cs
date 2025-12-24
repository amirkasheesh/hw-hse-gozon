using Gozon.Contracts;

public sealed class PaymentOperation
{
    public Guid OrderId { get; set; }

    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }

    public PaymentDecision Decision { get; set; }
    public string? Reason { get; set; }

    public Guid ResultMessageId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
