using StackExchange.Redis;

namespace Core.Hosting;

internal static class ValkeyDefaults
{
    private const int AsyncTimeoutMilliseconds = 500;
    private const int ConnectTimeoutMilliseconds = 2000;

    public static void Configure(ConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AsyncTimeout = AsyncTimeoutMilliseconds;
        options.ConnectTimeout = ConnectTimeoutMilliseconds;
    }
}
