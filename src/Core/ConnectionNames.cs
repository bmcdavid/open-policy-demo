namespace Core;

/// <summary>Resource names shared by the AppHost and the services it wires up (Aspire connection string names).</summary>
public static class ConnectionNames
{
    public const string Cosmos = "cosmos";
    public const string RabbitMq = "rabbitmq";
    public const string Valkey = "valkey";
}
