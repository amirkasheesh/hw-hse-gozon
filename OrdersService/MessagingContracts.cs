namespace Gozon.Contracts;

public static class BusTopology
{
    public const string OrdersExchange = "gozon.orders";
    public const string PaymentsExchange = "gozon.payments";

    public const string InitiatePaymentRoutingKey = "payments.initiate";
    public const string PaymentResultRoutingKey = "orders.payment_result";

    public const string PaymentsInitiateQueue = "payments.initiate_queue";
    public const string OrdersPaymentResultQueue = "orders.payment_results";
}

public enum PaymentDecision
{
    Approved = 1,
    Declined = 2
}

public sealed record InitiatePaymentCommand(
    Guid MessageId,
    Guid OrderId,
    string UserId,
    decimal Amount,
    DateTimeOffset RequestedAtUtc
);

public sealed record PaymentResultEvent(
    Guid MessageId,
    Guid OrderId,
    string UserId,
    decimal Amount,
    PaymentDecision Decision,
    string? Reason,
    DateTimeOffset OccurredAtUtc
);
