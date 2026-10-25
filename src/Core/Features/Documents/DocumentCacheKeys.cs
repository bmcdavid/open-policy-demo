namespace Core.Features.Documents;

/// <summary>The version segment must change whenever the cached encoding changes, so old entries are never decoded as the new format.</summary>
internal static class DocumentCacheKeys
{
    private const string _version = "v2";

    public static string Document(Guid id) => $"document:{_version}:{id}";

    public static string TenantList(string tenant) => $"documents:{_version}:{tenant}";

    /// <summary>Every entry a change to this document makes stale: the document itself (when known) and its tenant's list.</summary>
    public static IReadOnlyCollection<string> For(string tenant, Guid? id) =>
        id is null ? [TenantList(tenant)] : [Document(id.Value), TenantList(tenant)];
}
