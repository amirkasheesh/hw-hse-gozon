using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OrdersService.Persistence;

namespace OrdersService.Hubs;

public sealed class OrdersHub : Hub
{
    private readonly OrdersDbContext _db;

    public OrdersHub(OrdersDbContext db)
    {
        _db = db;
    }

    public async Task Subscribe(string orderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"order:{orderId}");

        if (!Guid.TryParse(orderId, out var id))
        {
            await Clients.Caller.SendAsync("OrderUpdated", new
            {
                orderId,
                status = "InvalidOrderId",
                reason = "orderId is not a GUID"
            });
            return;
        }

        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            await Clients.Caller.SendAsync("OrderUpdated", new
            {
                orderId,
                status = "NotFound",
                reason = "Order not found"
            });
            return;
        }

        await Clients.Caller.SendAsync("OrderUpdated", new
        {
            orderId = order.Id,
            status = order.Status.ToString(),
            reason = (string?)null
        });
    }

    public Task JoinOrder(string orderId) => Subscribe(orderId);

    public Task LeaveOrder(string orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"order:{orderId}");
}
