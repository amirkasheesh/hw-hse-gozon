using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace OrdersService.Messaging;

public sealed class RabbitMqConnectionProvider : IDisposable
{
    private readonly IConnection _connection;

    public RabbitMqConnectionProvider(IOptions<RabbitMqOptions> options)
    {
        var o = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = o.Host,
            Port = o.Port,
            UserName = o.Username,
            Password = o.Password,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection("orders-service");
    }

    public IModel CreateChannel() => _connection.CreateModel();

    public void Dispose()
    {
        try { _connection.Close(); } catch { }
        _connection.Dispose();
    }
}
