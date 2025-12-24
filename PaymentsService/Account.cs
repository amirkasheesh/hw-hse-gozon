public sealed class Account
{
    public string UserId { get; set; } = default!;
    public decimal Balance { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
