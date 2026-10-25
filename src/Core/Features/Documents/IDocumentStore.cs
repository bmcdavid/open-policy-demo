namespace Core.Features.Documents;

public interface IDocumentStore
{
    Task<IReadOnlyList<Document>> ListAsync(string tenant, CancellationToken cancellationToken);

    Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<Document> AddAsync(string tenant, string title, CancellationToken cancellationToken);

    Task<Document?> UpdateAsync(Guid id, string title, CancellationToken cancellationToken);

    /// <summary>Removes the document and returns it as it was, or null when it did not exist.</summary>
    Task<Document?> RemoveAsync(Guid id, CancellationToken cancellationToken);
}
