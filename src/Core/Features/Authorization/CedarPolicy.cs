using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Features.Authorization;

public static class CedarPolicy
{
    private const string _decisionPath = "v1/is_authorized";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static DecidePolicy Decide(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return (input, cancellationToken) => DecideAsync(http, input, cancellationToken);
    }

    private static async Task<PolicyDecision> DecideAsync(HttpClient http, PolicyInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            var cedarRequest = new CedarRequest(
                Principal: $"User::\"{input.Subject.Id}\"",
                Action: $"Action::\"{input.Action}\"",
                Resource: $"Resource::\"{input.Resource.Type}\"",
                Context: new CedarContext(
                    Roles: input.Subject.Roles,
                    Tenant: input.Subject.Tenant,
                    ResourceTenant: input.Resource.Tenant,
                    Environment: input.Environment));

            using var response = await http.PostAsJsonAsync(_decisionPath, cedarRequest, _json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PolicyEngineUnavailableException($"Cedar returned {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<CedarResponse>(_json, cancellationToken) ?? throw new PolicyEngineUnavailableException("Cedar returned no decision; is cedar-agent running?");
            var allow = string.Equals(body.Decision, "Allow", StringComparison.OrdinalIgnoreCase);
            if (allow)
            {
                return new PolicyDecision(true, []);
            }

            var reasons = (body.Diagnostics?.Reason ?? body.Diagnostics?.Reasons ?? []).ToList();
            if (reasons.Count == 0)
            {
                reasons.Add("role does not permit action");
            }

            reasons.Sort(StringComparer.Ordinal);
            return new PolicyDecision(false, reasons);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new PolicyEngineUnavailableException("Cedar request failed", ex);
        }
    }

    private sealed record CedarRequest(
        [property: JsonPropertyName("principal")] string Principal,
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("context")] CedarContext Context);

    private sealed record CedarContext(
        [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
        [property: JsonPropertyName("tenant")] string Tenant,
        [property: JsonPropertyName("resource_tenant")] string ResourceTenant,
        [property: JsonPropertyName("environment")] string Environment);

    private sealed record CedarResponse(
        [property: JsonPropertyName("decision")] string Decision,
        [property: JsonPropertyName("diagnostics")] CedarDiagnostics? Diagnostics);

    private sealed record CedarDiagnostics(
        [property: JsonPropertyName("reason")] IReadOnlyList<string>? Reason,
        [property: JsonPropertyName("reasons")] IReadOnlyList<string>? Reasons,
        [property: JsonPropertyName("errors")] IReadOnlyList<string>? Errors);
}
