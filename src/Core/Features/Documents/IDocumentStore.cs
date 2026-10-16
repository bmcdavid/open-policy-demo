namespace Core.Features.Documents;

public interface IDocumentStore
{
    IReadOnlyList<Document> List(string tenant);

    Document? Find(Guid id);

    Document Add(string tenant, string title);

    Document? Update(Guid id, string title);

    bool Remove(Guid id);
}
