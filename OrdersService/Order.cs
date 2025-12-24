namespace OrdersService.Domain;

public sealed class Order
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public decimal Amount { get; set; }
    public OrderStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
