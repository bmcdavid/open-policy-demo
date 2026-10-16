using System.Collections.Concurrent;

namespace Core.Features.Documents;

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<Guid, Document> _documents = new();

    public InMemoryDocumentStore()
    {
        Add("acme", "Acme quarterly plan");
        Add("globex", "Globex roadmap");
    }

    public IReadOnlyList<Document> List(string tenant) =>
        _documents.Values.Where(d => d.Tenant == tenant).OrderBy(d => d.Title, StringComparer.Ordinal).ToList();

    public Document? Find(Guid id) => _documents.GetValueOrDefault(id);

    public Document Add(string tenant, string title)
    {
        var document = new Document(Guid.NewGuid(), tenant, title);
        _documents[document.Id] = document;
        return document;
    }

    public Document? Update(Guid id, string title)
    {
        while (_documents.TryGetValue(id, out var current))
        {
            var updated = current with { Title = title };
            if (_documents.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }

        return null;
    }

    public bool Remove(Guid id) => _documents.TryRemove(id, out _);
}
