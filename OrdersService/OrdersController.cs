using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrdersService.Domain; 
using OrdersService.Persistence; 
using System.Text.Json;

namespace OrdersService.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly OrdersDbContext _db;
    
    private static readonly JsonSerializerOptions _jsonOptions = new() 
    { 
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
    };

    public OrdersController(OrdersDbContext db)
    {
        _db = db;
    }

    public record CreateOrderRequest(string UserId, decimal Amount);

    [HttpPost]
    public async Task<ActionResult<Order>> Create([FromBody] CreateOrderRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.UserId))
            return BadRequest("userId обязателен");

        if (req.Amount <= 0)
            return BadRequest("amount должен быть > 0");

        var now = DateTimeOffset.UtcNow;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = req.UserId.Trim(),
            Amount = req.Amount,
            Status = OrderStatus.New,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var messageId = Guid.NewGuid();

        var payload = new 
        {
            MessageId = messageId,
            OrderId = order.Id,
            UserId = order.UserId,
            Amount = order.Amount,
            RequestedAtUtc = now
        };

        var outboxMessage = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "InitiatePayment",
            OccurredAtUtc = now,
            MessageId = messageId,
            PayloadJson = JsonSerializer.Serialize(payload, _jsonOptions)
        };

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try 
        {
            _db.Orders.Add(order);
            _db.Outbox.Add(outboxMessage);
            
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Order>> GetById(Guid id, CancellationToken ct)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null) 
            return NotFound();

        return Ok(order);
    }

    [HttpGet]
    public async Task<ActionResult<List<Order>>> GetAll(CancellationToken ct)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(20)
            .ToListAsync(ct);

        return Ok(orders);
    }
}