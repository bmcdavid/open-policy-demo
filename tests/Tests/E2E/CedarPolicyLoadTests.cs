using System.IO;
using System.Net.Http.Json;
using System.Text.Json;
using CedarPolicyTool;
using AwesomeAssertions;
using Xunit;

namespace Tests.E2E;

/// <summary>Catches invalid Cedar that the AppHost's splitter accepts, such as a bad expression inside <c>when</c>.</summary>
[Collection(AppHostDefinition.Name)]
public sealed class CedarPolicyLoadTests(AppHostFixture fixture)
{
    [Fact]
    public async Task AgentLoadsEveryPolicyInAuthzCedar()
    {
        var source = await File.ReadAllTextAsync(RepoPath("policies", "authz.cedar"), TestContext.Current.CancellationToken);
        var expected = CedarPolicySplitter.Split(source).Policies.Select(p => p.Id);

        using var http = new HttpClient { BaseAddress = fixture.CedarUri };
        var loaded = await http.GetFromJsonAsync<JsonElement>(new Uri("v1/policies", UriKind.Relative), TestContext.Current.CancellationToken);

        loaded.EnumerateArray().Select(p => p.GetProperty("id").GetString()).Should().BeEquivalentTo(expected);
    }

    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "opa.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine([dir?.FullName ?? throw new InvalidOperationException("repo root not found"), .. parts]);
    }
}
