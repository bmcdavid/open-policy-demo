using Microsoft.Extensions.Logging;

namespace Core.Features.Documents;

/// <summary>Read-through cache over another store; created through <see cref="DocumentCache.Wrap"/>.</summary>
internal sealed partial class CachedDocumentStore(IDocumentStore inner, ICacheBackend backend, ILogger logger) : IDocumentStore
{
    private static readonly TimeSpan _timeToLive = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<Document>> ListAsync(string tenant, CancellationToken cancellationToken)
    {
        var key = DocumentCacheKeys.TenantList(tenant);
        var cached = await TryGetAsync(key, DocumentCacheSerializer.DeserializeList, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var documents = await inner.ListAsync(tenant, cancellationToken);
        await TrySetAsync(key, DocumentCacheSerializer.Serialize(documents), cancellationToken);
        return documents;
    }

    public async Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var key = DocumentCacheKeys.Document(id);
        var cached = await TryGetAsync(key, DocumentCacheSerializer.DeserializeDocument, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var document = await inner.FindAsync(id, cancellationToken);
        if (document is not null)
        {
            await TrySetAsync(key, DocumentCacheSerializer.Serialize(document), cancellationToken);
        }

        return document;
    }

    public async Task<Document> AddAsync(string tenant, string title, CancellationToken cancellationToken)
    {
        var document = await inner.AddAsync(tenant, title, cancellationToken);
        await TryRemoveAsync(DocumentCacheKeys.For(tenant, null), cancellationToken);
        return document;
    }

    public async Task<Document?> UpdateAsync(Guid id, string title, CancellationToken cancellationToken)
    {
        var updated = await inner.UpdateAsync(id, title, cancellationToken);
        if (updated is not null)
        {
            await TryRemoveAsync(DocumentCacheKeys.For(updated.Tenant, id), cancellationToken);
        }

        return updated;
    }

    public async Task<Document?> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var removed = await inner.RemoveAsync(id, cancellationToken);
        if (removed is not null)
        {
            await TryRemoveAsync(DocumentCacheKeys.For(removed.Tenant, id), cancellationToken);
        }

        return removed;
    }

    private async Task<T?> TryGetAsync<T>(string key, Func<ReadOnlyMemory<byte>, T> decode, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var bytes = await backend.GetAsync(key, cancellationToken);
            return bytes is null ? null : decode(bytes);
        }
        catch (CacheUnavailableException ex)
        {
            LogCacheUnavailable(ex, "get", key);
            return null;
        }
        catch (Exception ex) when (DocumentCacheSerializer.IsDecodeFailure(ex))
        {
            LogCacheUnavailable(ex, "get", key);
            return null;
        }
    }

    private async Task TrySetAsync(string key, byte[] value, CancellationToken cancellationToken)
    {
        try
        {
            await backend.SetAsync(key, value, _timeToLive, cancellationToken);
        }
        catch (CacheUnavailableException ex)
        {
            LogCacheUnavailable(ex, "set", key);
        }
    }

    private async Task TryRemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        try
        {
            await backend.RemoveAsync(keys, cancellationToken);
        }
        catch (CacheUnavailableException ex)
        {
            LogCacheUnavailable(ex, "remove", string.Join(',', keys));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Document cache {Operation} failed for {Key}; falling back to the store")]
    private partial void LogCacheUnavailable(Exception exception, string operation, string key);
}
