using Gozon.Contracts;

public sealed class InitiatePaymentConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _sp;
    private readonly RabbitMqConnectionProvider _mq;
    private readonly ILogger<InitiatePaymentConsumer> _log;

    public InitiatePaymentConsumer(IServiceProvider sp, RabbitMqConnectionProvider mq, ILogger<InitiatePaymentConsumer> log)
    {
        _sp = sp;
        _mq = mq;
        _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ch = _mq.CreateChannel();

        ch.ExchangeDeclare(BusTopology.OrdersExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        ch.QueueDeclare(BusTopology.PaymentsInitiateQueue, durable: true, exclusive: false, autoDelete: false);
        ch.QueueBind(BusTopology.PaymentsInitiateQueue, BusTopology.OrdersExchange, BusTopology.InitiatePaymentRoutingKey);

        ch.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var cmd = JsonSerializer.Deserialize<InitiatePaymentCommand>(json, JsonOpts)
                          ?? throw new InvalidOperationException("Не удалось распарсить InitiatePaymentCommand");

                await Handle(cmd, stoppingToken);

                ch.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ошибка обработки команды оплаты, сообщение будет переотправлено");
                ch.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        ch.BasicConsume(BusTopology.PaymentsInitiateQueue, autoAck: false, consumer: consumer);

        stoppingToken.Register(() =>
        {
            try { ch.Close(); } catch { }
            ch.Dispose();
        });

        return Task.CompletedTask;
    }

    private async Task Handle(InitiatePaymentCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.UserId)) return;
        if (cmd.Amount <= 0) return;

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var now = DateTimeOffset.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var inboxInserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO inbox_messages (message_id, received_at_utc)
            VALUES ({cmd.MessageId}, {now})
            ON CONFLICT (message_id) DO NOTHING;
        ", ct);

        if (inboxInserted == 0)
        {
            await tx.CommitAsync(ct);
            return;
        }

        var op = await db.Operations.FirstOrDefaultAsync(o => o.OrderId == cmd.OrderId, ct);

        PaymentDecision decision;
        string? reason;
        Guid resultMessageId;

        if (op is not null)
        {
            decision = op.Decision;
            reason = op.Reason;
            resultMessageId = op.ResultMessageId;
        }
        else
        {
            var u = cmd.UserId.Trim();

            var debitRows = await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE accounts
                SET balance = balance - {cmd.Amount}, updated_at_utc = {now}
                WHERE user_id = {u} AND balance >= {cmd.Amount};
            ", ct);

            if (debitRows == 1)
            {
                decision = PaymentDecision.Approved;
                reason = null;
            }
            else
            {
                var hasAccount = await db.Accounts.AnyAsync(a => a.UserId == u, ct);
                decision = PaymentDecision.Declined;
                reason = hasAccount ? "Недостаточно средств" : "Счёт не найден";
            }

            resultMessageId = Guid.NewGuid();

            db.Operations.Add(new PaymentOperation
            {
                OrderId = cmd.OrderId,
                UserId = u,
                Amount = cmd.Amount,
                Decision = decision,
                Reason = reason,
                ResultMessageId = resultMessageId,
                CreatedAtUtc = now
            });

            await db.SaveChangesAsync(ct);
        }

        var evt = new PaymentResultEvent(
            MessageId: resultMessageId,
            OrderId: cmd.OrderId,
            UserId: cmd.UserId.Trim(),
            Amount: cmd.Amount,
            Decision: decision,
            Reason: reason,
            OccurredAtUtc: now
        );

        var exists = await db.Outbox.AnyAsync(x => x.MessageId == resultMessageId, ct);
        if (!exists)
        {
            db.Outbox.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                MessageId = resultMessageId,
                Type = typeof(PaymentResultEvent).FullName!,
                PayloadJson = JsonSerializer.Serialize(evt, JsonOpts),
                OccurredAtUtc = now
            });

            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
    }
}
