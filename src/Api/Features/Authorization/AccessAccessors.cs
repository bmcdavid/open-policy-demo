using Core.Features.Documents;

namespace Api.Features.Authorization;

/// <summary>The document <see cref="AccessFilter"/> loaded and the policy allowed. Only readable on a route declared with <c>AuthorizeExisting</c>.</summary>
internal sealed class AuthorizedDocument
{
    private Document? _value;

    public Document Value => _value ?? throw AccessFilter.NotAuthorized(nameof(AuthorizedDocument));

    internal void Set(Document document) => _value = document;
}

/// <summary>The tenant <see cref="AccessFilter"/> authorized the request for. Only readable on a route declared with <c>AuthorizeInTenant</c>.</summary>
internal sealed class TenantScope
{
    private string? _tenant;

    public string Tenant => _tenant ?? throw AccessFilter.NotAuthorized(nameof(TenantScope));

    internal void Set(string tenant) => _tenant = tenant;
}

/// <summary>A request body that names the tenant it acts in. <see cref="AccessFilter"/> prefers it to the <c>tenant</c> query value.</summary>
internal interface ITenantRequest
{
    string? Tenant { get; }
}
