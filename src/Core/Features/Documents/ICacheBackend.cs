namespace Core.Features.Documents;

/// <summary>
/// The byte-level cache seam: a key/value store with expiry. The three operations share a connection, a key space and one
/// error contract, so they form one cohesive interface: every failure is reported as <see cref="CacheUnavailableException"/>.
/// </summary>
public interface ICacheBackend
{
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan timeToLive, CancellationToken cancellationToken);

    Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}

public sealed class CacheUnavailableException : Exception
{
    public CacheUnavailableException()
    {
    }

    public CacheUnavailableException(string message)
        : base(message)
    {
    }

    public CacheUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
