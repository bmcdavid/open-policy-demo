using System.Text.Json;
using Core.Features.Cosmos;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.Azure.Cosmos;

namespace Core.Features.Audit;

public static class CosmosAudit
{
    public static UpsertAuditEntry Upsert(Database database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var container = database.GetContainer(CosmosNames.Audit);

        return async (entry, cancellationToken) =>
        {
            try
            {
                await container.UpsertItemAsync(entry, new PartitionKey(entry.Tenant), cancellationToken: cancellationToken);
            }
            catch (CosmosException ex)
            {
                throw new StoreUnavailableException("Cosmos audit write failed", ex);
            }
        };
    }

    public static QueryAudit Query(Database database)
    {
        ArgumentNullException.ThrowIfNull(database);
        var container = database.GetContainer(CosmosNames.Audit);

        return async (query, cancellationToken) =>
        {
            var definition = BuildQuery(query);
            var options = new QueryRequestOptions { PartitionKey = new PartitionKey(query.Tenant), MaxItemCount = query.PageSize };

            try
            {
                using var iterator = container.GetItemQueryIterator<AuditEntry>(definition, query.ContinuationToken, options);
                var page = await iterator.ReadNextAsync(cancellationToken);
                return new AuditPage(page.ToList(), page.ContinuationToken);
            }
            catch (CosmosException ex)
            {
                throw new StoreUnavailableException("Cosmos audit query failed", ex);
            }
        };
    }

    // occurredAt is stored as the serializer's ISO-8601 string, so bounds are formatted by the same serializer to compare correctly.
    private static string Iso(DateTimeOffset value) =>
        JsonSerializer.Serialize(value.ToUniversalTime(), EventJson.Options).Trim('"');

    internal static QueryDefinition BuildQuery(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sql = "SELECT * FROM c WHERE c.tenant = @tenant";
        if (query.From is not null)
        {
            sql += " AND c.occurredAt >= @from";
        }

        if (query.To is not null)
        {
            sql += " AND c.occurredAt <= @to";
        }

        var definition = new QueryDefinition(sql + " ORDER BY c.occurredAt DESC").WithParameter("@tenant", query.Tenant);
        if (query.From is { } from)
        {
            definition = definition.WithParameter("@from", Iso(from));
        }

        if (query.To is { } to)
        {
            definition = definition.WithParameter("@to", Iso(to));
        }

        return definition;
    }
}
