using Microsoft.EntityFrameworkCore;
using OrdersService.Hubs;
using OrdersService.Hosted;
using OrdersService.Messaging;
using OrdersService.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});




builder.Services.AddDbContext<OrdersDbContext>(opt =>
{
    var cs = builder.Configuration.GetConnectionString("OrdersDb");
    opt.UseNpgsql(cs);
});

builder.Services.AddSignalR();

builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
builder.Services.AddSingleton<RabbitMqConnectionProvider>();

builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<PaymentResultConsumer>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapControllers();
app.MapHub<OrdersHub>("/hubs/orders");

app.Run();
