using Core.Features.Events;
using Microsoft.Extensions.Logging;

namespace Core.Features.Documents;

/// <summary>Evicts the cache entries a document change made stale. Used by the Worker for changes made by other writers.</summary>
public delegate Task InvalidateDocument(DocumentChanged change, CancellationToken cancellationToken);

/// <summary>
/// The document cache as one module: the key scheme and its version, the protobuf encoding, the 60 second TTL, and the
/// rule for what a change invalidates all live here. Authorization decisions are never cached, only document data.
/// </summary>
public sealed class DocumentCache(ICacheBackend backend, ILogger<DocumentCache> logger)
{
    /// <summary>Read-through, evict-on-write. Cache outages and undecodable entries fall through to the inner store.</summary>
    public IDocumentStore Wrap(IDocumentStore inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new CachedDocumentStore(inner, backend, logger);
    }

    /// <summary>Evicts the entries the change made stale. Unlike reads, an outage propagates so the caller can retry.</summary>
    public Task InvalidateAsync(DocumentChanged change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        return backend.RemoveAsync(DocumentCacheKeys.For(change.Tenant, change.DocumentId), cancellationToken);
    }
}
