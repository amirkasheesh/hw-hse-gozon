using Gozon.Contracts;
using Microsoft.EntityFrameworkCore;
using OrdersService.Messaging;
using OrdersService.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OrdersService.Hosted;

public sealed class OutboxPublisher : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly RabbitMqConnectionProvider _mq;
    private readonly ILogger<OutboxPublisher> _log;

    public OutboxPublisher(IServiceProvider sp, RabbitMqConnectionProvider mq, ILogger<OutboxPublisher> log)
    {
        _sp = sp;
        _mq = mq;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var ch = _mq.CreateChannel();

        ch.ExchangeDeclare(BusTopology.OrdersExchange, ExchangeType.Topic, durable: true, autoDelete: false);

        ch.QueueDeclare(queue: "payments.initiate_queue", durable: true, exclusive: false, autoDelete: false);
        
        ch.QueueBind(
            queue: "payments.initiate_queue", 
            exchange: BusTopology.OrdersExchange, 
            routingKey: BusTopology.InitiatePaymentRoutingKey);

        ch.ConfirmSelect();

        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            { 
                await PublishBatch(ch, stoppingToken); 
            }
            catch (Exception ex) 
            { 
                _log.LogError(ex, "Критическая ошибка цикла публикации Outbox"); 
            }

            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task PublishBatch(IModel ch, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var batch = await db.Outbox
            .Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(50)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        foreach (var msg in batch)
        {
            bool published = TryPublishOne(ch, msg, out string? error);

            if (published)
            {
                msg.ProcessedAtUtc = DateTimeOffset.UtcNow;
                msg.LastError = null;
            }
            else
            {
                msg.LastError = error;
                _log.LogWarning("Не удалось опубликовать сообщение {MessageId}. Ошибка: {Error}", msg.MessageId, error);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private bool TryPublishOne(IModel ch, OutboxMessage msg, out string? error)
    {
        error = null;
        var messageId = msg.MessageId.ToString();
        bool returned = false;
        string? returnReason = null;

        EventHandler<BasicReturnEventArgs> onReturn = (_, ea) =>
        {
            if (ea.BasicProperties.MessageId == messageId)
            {
                returned = true;
                returnReason = $"Сообщение возвращено брокером (код {ea.ReplyCode}): {ea.ReplyText}";
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

            var body = Encoding.UTF8.GetBytes(msg.PayloadJson);

            ch.BasicPublish(
                exchange: BusTopology.OrdersExchange,
                routingKey: BusTopology.InitiatePaymentRoutingKey,
                mandatory: true,
                basicProperties: props,
                body: body
            );

            bool acked = ch.WaitForConfirms(TimeSpan.FromSeconds(5));

            if (!acked)
            {
                error = "Timeout: брокер не прислал ACK вовремя";
                return false;
            }

            if (returned)
            {
                error = returnReason ?? "Сообщение возвращено (unroutable)";
                return false;
            }

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
