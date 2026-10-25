using System.Collections.Concurrent;

namespace Core.Features.Documents;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<Guid, Document> _documents = new();

    public InMemoryDocumentStore()
    {
        foreach (var seed in DocumentSeed.Documents)
        {
            _documents[seed.Id] = seed;
        }
    }

    public Task<IReadOnlyList<Document>> ListAsync(string tenant, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Document>>(
            [.. _documents.Values.Where(d => d.Tenant == tenant).OrderBy(d => d.Title, StringComparer.Ordinal)]);

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.GetValueOrDefault(id));

    public Task<Document> AddAsync(string tenant, string title, CancellationToken cancellationToken)
    {
        var document = new Document(Guid.NewGuid(), tenant, title);
        _documents[document.Id] = document;
        return Task.FromResult(document);
    }

    public Task<Document?> UpdateAsync(Guid id, string title, CancellationToken cancellationToken)
    {
        while (_documents.TryGetValue(id, out var current))
        {
            var updated = current with { Title = title };
            if (_documents.TryUpdate(id, updated, current))
            {
                return Task.FromResult<Document?>(updated);
            }
        }

        return Task.FromResult<Document?>(null);
    }

    public Task<Document?> RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.TryRemove(id, out var removed) ? removed : null);
}
