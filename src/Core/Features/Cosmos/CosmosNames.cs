namespace Core.Features.Cosmos;

public static class CosmosNames
{
    public const string Database = "policy-demo";
    public const string Documents = "documents";
    public const string Audit = "audit";
    public const string Leases = "leases";
    public const string PartitionKeyPath = "/tenant";

    public const string DocumentType = "document";
    public const string OutboxType = "outbox";

    /// <summary>Outbox items expire on their own; a relay lagging longer than this would lose events.</summary>
    public static readonly TimeSpan OutboxTimeToLive = TimeSpan.FromDays(7);
}
