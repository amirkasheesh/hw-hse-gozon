var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/orders/swagger/v1/swagger.json", "Orders Service");
    c.SwaggerEndpoint("/swagger/payments/swagger/v1/swagger.json", "Payments Service");
    
});

app.UseWebSockets();
app.MapReverseProxy();

app.Run();
