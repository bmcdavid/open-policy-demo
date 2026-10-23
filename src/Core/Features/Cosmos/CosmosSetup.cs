using System.Net;
using Core.Features.Documents;
using Microsoft.Azure.Cosmos;

namespace Core.Features.Cosmos;

public static class CosmosSetup
{
    public static void ConfigureClientOptions(CosmosClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseSystemTextJsonSerializerWithOptions = Events.EventJson.Options;
        options.ConnectionMode = ConnectionMode.Gateway;
    }

    /// <summary>Idempotent: creates the database, containers and seed documents when missing.</summary>
    public static async Task EnsureCreatedAsync(Database database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);

        database = (await database.Client.CreateDatabaseIfNotExistsAsync(database.Id, cancellationToken: cancellationToken)).Database;
        var documents = (await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(CosmosNames.Documents, CosmosNames.PartitionKeyPath) { DefaultTimeToLive = -1 },
            cancellationToken: cancellationToken)).Container;
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(CosmosNames.Audit, CosmosNames.PartitionKeyPath), cancellationToken: cancellationToken);
        await database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(CosmosNames.Leases, "/id"), cancellationToken: cancellationToken);

        foreach (var seed in DocumentSeed.Documents)
        {
            try
            {
                await documents.CreateItemAsync(DocumentItem.From(seed), new PartitionKey(seed.Tenant), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Already seeded by the other host.
            }
        }
    }
}
