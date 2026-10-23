namespace Core.Features.Documents;

public sealed record Document(Guid Id, string Tenant, string Title);
