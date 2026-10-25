using StackExchange.Redis;

namespace Core.Hosting;

internal static class ValkeyDefaults
{
    private const int _asyncTimeoutMilliseconds = 500;
    private const int _connectTimeoutMilliseconds = 2000;

    public static void Configure(ConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AsyncTimeout = _asyncTimeoutMilliseconds;
        options.ConnectTimeout = _connectTimeoutMilliseconds;
    }
}
