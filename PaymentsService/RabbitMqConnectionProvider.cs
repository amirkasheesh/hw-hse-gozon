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

        _connection = factory.CreateConnection("payments-service");
    }

    public IModel CreateChannel() => _connection.CreateModel();

    public void Dispose() => _connection.Dispose();
}
