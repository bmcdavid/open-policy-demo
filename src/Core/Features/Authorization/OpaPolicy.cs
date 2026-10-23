using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Features.Authorization;

public static class OpaPolicy
{
    private const string DecisionPath = "v1/data/authz/decision";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static DecidePolicy Decide(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return (input, cancellationToken) => DecideAsync(http, input, cancellationToken);
    }

    private static async Task<PolicyDecision> DecideAsync(HttpClient http, PolicyInput input, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(DecisionPath, new OpaRequest(input), Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new PolicyEngineUnavailableException($"OPA returned {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<OpaResponse>(Json, cancellationToken);
            return body?.Result is { } result
                ? new PolicyDecision(result.Allow, result.Reasons ?? [])
                : throw new PolicyEngineUnavailableException("OPA returned no decision; is the policy bundle loaded?");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new PolicyEngineUnavailableException("OPA request failed", ex);
        }
    }

    private sealed record OpaRequest(PolicyInput Input);

    private sealed record OpaResponse(OpaResult? Result);

    private sealed record OpaResult(bool Allow, [property: JsonPropertyName("reasons")] IReadOnlyList<string>? Reasons);
}
