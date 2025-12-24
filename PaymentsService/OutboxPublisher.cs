using System.Text;
using System.Collections.Concurrent;
using Gozon.Contracts;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public sealed class OutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly RabbitMqConnectionProvider _mq;
    private readonly ILogger<OutboxPublisher> _log;

    private const string PaymentResultsQueue = "orders.payment_results";

    public OutboxPublisher(IServiceProvider sp, RabbitMqConnectionProvider mq, ILogger<OutboxPublisher> log)
    {
        _sp = sp;
        _mq = mq;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ch = _mq.CreateChannel();

        ch.ExchangeDeclare(
            exchange: BusTopology.PaymentsExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false
        );

        ch.QueueDeclare(
            queue: PaymentResultsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        ch.QueueBind(
            queue: PaymentResultsQueue,
            exchange: BusTopology.PaymentsExchange,
            routingKey: BusTopology.PaymentResultRoutingKey
        );

        ch.ConfirmSelect();

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PublishBatch(ch, stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Ошибка публикации outbox"); }

            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task PublishBatch(IModel ch, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();

        var batch = await db.Outbox
            .Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(50)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var msg in batch)
        {
            var ok = TryPublishOne(ch, msg, out var err);

            if (ok)
            {
                msg.ProcessedAtUtc = DateTimeOffset.UtcNow;
                msg.LastError = null;
            }
            else
            {
                msg.LastError = err ?? "неизвестная ошибка";
                _log.LogWarning(
                    "Не удалось опубликовать outbox {MessageId}. Причина: {Error}",
                    msg.MessageId, msg.LastError
                );
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private bool TryPublishOne(IModel ch, dynamic msg, out string? error)
    {

        var messageId = msg.MessageId.ToString();
        var returned = false;
        string? returnReason = null;

        EventHandler<BasicReturnEventArgs>? onReturn = (_, ea) =>
        {
            var returnedId = ea.BasicProperties?.MessageId;
            if (returnedId == messageId)
            {
                returned = true;
                returnReason = $"Возвращено брокером: replyCode = {ea.ReplyCode}, replyText = {ea.ReplyText}, exchange = {ea.Exchange}, routingKey = {ea.RoutingKey}";
            }
        };

        ch.BasicReturn += onReturn;

        try
        {
            var props = ch.CreateBasicProperties();
            props.Persistent = true;
            props.MessageId = messageId;
            props.Type = msg.Type;
            props.ContentType = "application/json";

            var body = Encoding.UTF8.GetBytes((string)msg.PayloadJson);

            ch.BasicPublish(
                exchange: BusTopology.PaymentsExchange,
                routingKey: BusTopology.PaymentResultRoutingKey,
                mandatory: true,
                basicProperties: props,
                body: body
            );

            var confirmed = ch.WaitForConfirms(TimeSpan.FromSeconds(3));
            if (!confirmed)
            {
                error = "Ошибка 1";
                return false;
            }

            if (returned)
            {
                error = returnReason ?? "Ошибка 2";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            ch.BasicReturn -= onReturn;
        }
    }
}
