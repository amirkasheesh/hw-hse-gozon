public sealed class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<PaymentOperation> Operations => Set<PaymentOperation>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("accounts");
            
            b.HasKey(x => x.UserId);
            
            b.Property(x => x.UserId)
                .HasColumnName("user_id")
                .IsRequired()
                .HasMaxLength(128);

            b.Property(x => x.Balance)
                .HasColumnName("balance")
                .HasColumnType("numeric(18,2)");

            b.Property(x => x.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            b.Property(x => x.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();
        });

        modelBuilder.Entity<InboxMessage>(b =>
    {
        b.ToTable("inbox_messages");
        b.HasKey(x => x.MessageId);

        b.Property(x => x.MessageId).HasColumnName("message_id");
        b.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
    });

    modelBuilder.Entity<PaymentOperation>(b =>
    {
        b.ToTable("payment_operations");
        b.HasKey(x => x.OrderId);

        b.Property(x => x.OrderId).HasColumnName("order_id");
        b.Property(x => x.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(128);
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");

        b.Property(x => x.Decision).HasColumnName("decision").IsRequired();
        b.Property(x => x.Reason).HasColumnName("reason");
        b.Property(x => x.ResultMessageId).HasColumnName("result_message_id").IsRequired();

        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    });

        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ProcessedAtUtc);
            b.HasIndex(x => x.MessageId).IsUnique();
            b.Property(x => x.Type).IsRequired().HasMaxLength(256);
            b.Property(x => x.PayloadJson).IsRequired();
            b.Property(x => x.OccurredAtUtc).IsRequired();
        });
    }
}
