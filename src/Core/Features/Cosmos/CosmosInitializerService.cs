using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.Features.Cosmos;

/// <summary>Creates the database, containers and seed data at startup. Retries because the emulator can accept connections before it is ready.</summary>
public sealed partial class CosmosInitializerService(Database database, ILogger<CosmosInitializerService> logger) : IHostedService
{
    private const int MaxAttempts = 20;
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await CosmosSetup.EnsureCreatedAsync(database, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is CosmosException or HttpRequestException)
            {
                LogNotReady(ex, attempt, MaxAttempts);
                await Task.Delay(Delay, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cosmos is not ready (attempt {Attempt} of {Max}); retrying")]
    private partial void LogNotReady(Exception exception, int attempt, int max);
}
