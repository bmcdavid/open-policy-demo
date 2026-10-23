using Core.Features.Documents;
using StackExchange.Redis;

namespace Core.Features.Valkey;

/// <summary>The cache seam over a Valkey (Redis protocol) connection.</summary>
public sealed class ValkeyCacheBackend(IConnectionMultiplexer connection) : ICacheBackend
{
    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
        RunAsync("get", async database =>
        {
            var value = await database.StringGetAsync(key);
            return value.IsNull ? null : (byte[]?)value;
        });

    public Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan timeToLive, CancellationToken cancellationToken) =>
        RunAsync("set", async database =>
        {
            await database.StringSetAsync(key, value, timeToLive);
            return true;
        });

    public Task RemoveAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        RunAsync("remove", async database =>
        {
            await database.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
            return true;
        });

    private async Task<T> RunAsync<T>(string operation, Func<IDatabase, Task<T>> run)
    {
        try
        {
            return await run(connection.GetDatabase());
        }
        catch (RedisException ex)
        {
            throw new CacheUnavailableException($"Valkey {operation} failed", ex);
        }
        catch (TimeoutException ex)
        {
            throw new CacheUnavailableException($"Valkey {operation} timed out", ex);
        }
    }
}
