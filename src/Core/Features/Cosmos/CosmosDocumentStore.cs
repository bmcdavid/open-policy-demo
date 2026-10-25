using System.Net;
using Core.Features.Documents;
using Core.Features.Events;
using Microsoft.Azure.Cosmos;

namespace Core.Features.Cosmos;

/// <summary>
/// Cosmos-backed store. Every write goes in one transactional batch with its <c>DocumentChanged</c> outbox item, so
/// the document and its event are committed together or not at all.
/// </summary>
public sealed class CosmosDocumentStore(Database database, TimeProvider? time = null) : IDocumentStore
{
    private const int MaxConflictRetries = 3;

    private readonly Container _documents = database.GetContainer(CosmosNames.Documents);

    public async Task<IReadOnlyList<Document>> ListAsync(string tenant, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type AND c.tenant = @tenant ORDER BY c.title")
            .WithParameter("@type", CosmosNames.DocumentType)
            .WithParameter("@tenant", tenant);

        var items = await QueryAsync(query, new QueryRequestOptions { PartitionKey = new PartitionKey(tenant) }, cancellationToken);
        return items.Select(i => i.ToDocument()).ToList();
    }

    public async Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await FindItemAsync(id, cancellationToken))?.ToDocument();

    public async Task<Document> AddAsync(string tenant, string title, CancellationToken cancellationToken)
    {
        var document = new Document(Guid.NewGuid(), tenant, title);

        await CommitAsync(tenant, document.Id, DocumentChangeKind.Created, title, batch => batch.CreateItem(DocumentItem.From(document)), cancellationToken);
        return document;
    }

    public async Task<Document?> UpdateAsync(Guid id, string title, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxConflictRetries; attempt++)
        {
            var current = await FindItemAsync(id, cancellationToken);
            if (current is null)
            {
                return null;
            }

            var updated = current with { Title = title };
            var result = await CommitAsync(
                current.Tenant,
                id,
                DocumentChangeKind.Updated,
                title,
                batch => batch.ReplaceItem(current.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = current.ETag }),
                cancellationToken);
            switch (result)
            {
                case HttpStatusCode.PreconditionFailed:
                    continue;
                case HttpStatusCode.NotFound:
                    return null;
                default:
                    return (updated with { ETag = null }).ToDocument();
            }
        }

        throw new StoreUnavailableException($"Document {id} kept changing; update abandoned after {MaxConflictRetries} attempts");
    }

    public async Task<Document?> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await FindItemAsync(id, cancellationToken);
        if (current is null)
        {
            return null;
        }

        var result = await CommitAsync(current.Tenant, id, DocumentChangeKind.Deleted, current.Title, batch => batch.DeleteItem(current.Id), cancellationToken);
        return result == HttpStatusCode.NotFound ? null : current.ToDocument();
    }

    /// <summary>Applies the write and its <c>DocumentChanged</c> outbox item in one transactional batch on the tenant's partition.</summary>
    private Task<HttpStatusCode> CommitAsync(
        string tenant,
        Guid id,
        DocumentChangeKind kind,
        string title,
        Func<TransactionalBatch, TransactionalBatch> write,
        CancellationToken cancellationToken)
    {
        var change = OutboxEvent.Create(tenant, EventTypes.DocumentChanged, new DocumentChanged(id, tenant, kind, title), time);
        var batch = write(_documents.CreateTransactionalBatch(new PartitionKey(tenant))).AddTo(change);
        return ExecuteAsync(batch, cancellationToken);
    }

    private async Task<DocumentItem?> FindItemAsync(Guid id, CancellationToken cancellationToken)
    {
        // The tenant is not known yet, so this crosses partitions. Acceptable for a demo; a real service would
        // carry the tenant in the route or look it up from the token.
        var query = new QueryDefinition("SELECT * FROM c WHERE c.type = @type AND c.id = @id")
            .WithParameter("@type", CosmosNames.DocumentType)
            .WithParameter("@id", id.ToString());

        var items = await QueryAsync(query, new QueryRequestOptions(), cancellationToken);
        return items.FirstOrDefault();
    }

    private async Task<List<DocumentItem>> QueryAsync(QueryDefinition query, QueryRequestOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var items = new List<DocumentItem>();
            using var iterator = _documents.GetItemQueryIterator<DocumentItem>(query, requestOptions: options);
            while (iterator.HasMoreResults)
            {
                items.AddRange(await iterator.ReadNextAsync(cancellationToken));
            }

            return items;
        }
        catch (CosmosException ex)
        {
            throw new StoreUnavailableException("Cosmos query failed", ex);
        }
    }

    /// <summary>Runs the batch and returns the overall status, throwing for anything other than success, 404 or 412.</summary>
    private static async Task<HttpStatusCode> ExecuteAsync(TransactionalBatch batch, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await batch.ExecuteAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return HttpStatusCode.OK;
            }

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.PreconditionFailed)
            {
                return response.StatusCode;
            }

            throw new StoreUnavailableException($"Cosmos batch failed with {(int)response.StatusCode} {response.ErrorMessage}");
        }
        catch (CosmosException ex)
        {
            throw new StoreUnavailableException("Cosmos batch failed", ex);
        }
    }
}
