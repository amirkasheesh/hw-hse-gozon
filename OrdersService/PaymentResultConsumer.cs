using Gozon.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OrdersService.Hubs;
using OrdersService.Messaging;
using OrdersService.Persistence;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OrdersService.Hosted;

public sealed class PaymentResultConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _sp;
    private readonly RabbitMqConnectionProvider _mq;
    private readonly IHubContext<OrdersHub> _hub;
    private readonly ILogger<PaymentResultConsumer> _log;

    public PaymentResultConsumer(IServiceProvider sp, RabbitMqConnectionProvider mq, IHubContext<OrdersHub> hub, ILogger<PaymentResultConsumer> log)
    {
        _sp = sp;
        _mq = mq;
        _hub = hub;
        _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ch = _mq.CreateChannel();

        ch.ExchangeDeclare(BusTopology.PaymentsExchange, ExchangeType.Topic, durable: true, autoDelete: false);
        ch.QueueDeclare(BusTopology.OrdersPaymentResultQueue, durable: true, exclusive: false, autoDelete: false);
        ch.QueueBind(BusTopology.OrdersPaymentResultQueue, BusTopology.PaymentsExchange, BusTopology.PaymentResultRoutingKey);

        ch.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.Received += async (_, ea) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                var evt = JsonSerializer.Deserialize<PaymentResultEvent>(json, JsonOpts)
                          ?? throw new InvalidOperationException("Не удалось распарсить PaymentResultEvent");

                await HandleEvent(evt, stoppingToken);

                ch.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ошибка обработки события оплаты, сообщение будет переотправлено");
                ch.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        ch.BasicConsume(BusTopology.OrdersPaymentResultQueue, autoAck: false, consumer: consumer);

        stoppingToken.Register(() =>
        {
            try { ch.Close(); } catch { }
            ch.Dispose();
        });

        return Task.CompletedTask;
    }

    private async Task HandleEvent(PaymentResultEvent evt, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        if (await db.Inbox.AnyAsync(x => x.MessageId == evt.MessageId, ct))
            return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.Inbox.Add(new InboxMessage
        {
            MessageId = evt.MessageId,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });

        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == evt.OrderId, ct);
        if (order is null)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        if (order.Status != Domain.OrderStatus.New)
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return;
        }

        order.Status = evt.Decision == PaymentDecision.Approved
            ? Domain.OrderStatus.Finished
            : Domain.OrderStatus.Cancelled;

        order.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _hub.Clients.Group($"order:{order.Id}")
            .SendAsync("OrderUpdated", new
            {
                orderId = order.Id,
                status = order.Status.ToString(),
                reason = evt.Reason
            }, ct);
    }
}
