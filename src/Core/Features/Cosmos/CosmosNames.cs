namespace Core.Features.Cosmos;

public static class CosmosNames
{
    /// <summary>Used when <c>Cosmos:Database</c> is not set (Aspire). Each Kubernetes environment sets its own.</summary>
    public const string DefaultDatabase = "policy-demo";
    public const string Documents = "documents";
    public const string Audit = "audit";
    public const string Leases = "leases";
    public const string PartitionKeyPath = "/tenant";

    public const string DocumentType = "document";
    public const string OutboxType = "outbox";

    /// <summary>Outbox items expire on their own; a relay lagging longer than this would lose events.</summary>
    public static readonly TimeSpan OutboxTimeToLive = TimeSpan.FromDays(7);
}
