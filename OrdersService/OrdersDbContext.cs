using Microsoft.EntityFrameworkCore;
using OrdersService.Domain;

namespace OrdersService.Persistence;

public sealed class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.UserId).IsRequired().HasMaxLength(128);
            b.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            b.Property(x => x.Status).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.Property(x => x.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            
            b.Property(x => x.Type).IsRequired().HasMaxLength(256);
            b.Property(x => x.PayloadJson).IsRequired();
            b.Property(x => x.OccurredAtUtc).IsRequired();
            b.Property(x => x.ProcessedAtUtc);
            
            b.HasIndex(x => x.ProcessedAtUtc); 
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.MessageId).IsUnique();
        });
    }
}