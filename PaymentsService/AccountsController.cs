[ApiController]
[Route("accounts")]
public sealed class AccountsController : ControllerBase
{
    private readonly PaymentsDbContext _db;

    public AccountsController(PaymentsDbContext db) => _db = db;

    public sealed record CreateAccountRequest(string UserId);
    public sealed record MoneyRequest(decimal Amount);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.UserId))
        {
            return BadRequest("userId обязателен");
        }

        var userId = req.UserId.Trim();

        var exists = await _db.Accounts.AnyAsync(a => a.UserId == userId, ct);
        if (exists)
        {
            return Conflict("Счёт для этого пользователя уже существует");
        }

        var now = DateTimeOffset.UtcNow;

        _db.Accounts.Add(new Account
        {
            UserId = userId,
            Balance = 0m,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await _db.SaveChangesAsync(ct);
        return Created($"/accounts/{userId}/balance", new { userId, balance = 0m });
    }

    [HttpGet("{userId}/balance")]
    public async Task<IActionResult> Balance(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId обязателен");
        }

        var acc = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId.Trim(), ct);
        if (acc is null)
        {
            return NotFound("Счёт не найден");
        }

        return Ok(new { userId = acc.UserId, balance = acc.Balance });
    }

    [HttpPost("{userId}/topup")]
    public async Task<IActionResult> TopUp(string userId, [FromBody] MoneyRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId обязателен");
        }
        if (req.Amount <= 0)
        {
            return BadRequest("amount должен быть > 0");
        }

        var u = userId.Trim();
        var now = DateTimeOffset.UtcNow;

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE accounts
            SET balance = balance + {req.Amount}, updated_at_utc = {now}
            WHERE user_id = {u};
        ", ct);

        if (rows == 0)
        {
            return NotFound("Счёт не найден");
        }

        var acc = await _db.Accounts.AsNoTracking().FirstAsync(a => a.UserId == u, ct);
        return Ok(new { userId = acc.UserId, balance = acc.Balance });
    }
}
