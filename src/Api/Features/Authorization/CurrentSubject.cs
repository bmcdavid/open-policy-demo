using System.Security.Claims;
using Core.Features.Authorization;

namespace Api.Features.Authorization;

internal static class CurrentSubject
{
    public const string SubjectClaim = "sub";
    public const string RolesClaim = "roles";
    public const string TenantClaim = "tenant";

    public static PolicySubject From(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new PolicySubject(
            user.FindFirstValue(SubjectClaim) ?? string.Empty,
            user.FindAll(RolesClaim).Select(c => c.Value).ToArray(),
            user.FindFirstValue(TenantClaim) ?? string.Empty);
    }
}
