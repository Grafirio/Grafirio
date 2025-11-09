namespace Grafirio.Shared.MassTransit.Options;

public class RabbitMqOptions
{
    public const string Key = "RabbitMQ";
    
    public string Host { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public ushort Port { get; set; } = 5672;
}
