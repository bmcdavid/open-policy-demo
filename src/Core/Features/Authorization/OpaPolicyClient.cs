using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Features.Authorization;

public sealed class OpaPolicyClient(HttpClient http) : IPolicyClient
{
    private const string DecisionPath = "v1/data/authz/decision";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PolicyDecision> DecideAsync(PolicyInput input, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(DecisionPath, new OpaRequest(input), Json, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new PolicyEngineUnavailableException($"OPA returned {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<OpaResponse>(Json, cancellationToken).ConfigureAwait(false);
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
