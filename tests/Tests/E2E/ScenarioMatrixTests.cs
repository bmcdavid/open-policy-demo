using System.Text;
using AwesomeAssertions;
using Core.Features.Authorization;
using Xunit;

namespace Tests.E2E;

/// <summary>Queries OPA directly with synthetic input for every user x tenant x environment x action.</summary>
[Collection(AppHostDefinition.Name)]
public sealed class ScenarioMatrixTests(AppHostFixture fixture, ITestOutputHelper output)
{
    private static readonly (string User, string[] Roles, string Tenant)[] Users =
    [
        ("reader-acme", ["reader"], "acme"),
        ("editor-acme", ["editor"], "acme"),
        ("admin-acme", ["admin"], "acme"),
        ("editor-globex", ["editor"], "globex"),
        ("admin-globex", ["admin"], "globex"),
        ("platform-admin", ["platform-admin"], "platform"),
    ];

    private static readonly string[] Tenants = ["acme", "globex"];
    private static readonly string[] Environments = ["dev", "staging", "prod"];

    private static readonly string[] Actions =
        [PolicyActions.List, PolicyActions.Read, PolicyActions.Create, PolicyActions.Update, PolicyActions.Delete, PolicyActions.Audit];

    /// <summary>Independent oracle for the policy; deliberately written separately from the rego.</summary>
    private static bool Expected(string[] roles, string tenant, string resourceTenant, string environment, string action)
    {
        var platform = roles.Contains("platform-admin");
        var admin = platform || roles.Contains("admin");
        var editor = admin || roles.Contains("editor");
        var reader = editor || roles.Contains("reader");

        var roleAllows = action switch
        {
            PolicyActions.List or PolicyActions.Read => reader,
            PolicyActions.Create or PolicyActions.Update => editor,
            PolicyActions.Delete or PolicyActions.Audit => admin,
            _ => false,
        };
        var tenantAllows = platform || tenant == resourceTenant;
        var prodAllows = environment != "prod" || action switch
        {
            PolicyActions.Create or PolicyActions.Update => admin,
            PolicyActions.Delete => platform,
            _ => true,
        };
        return roleAllows && tenantAllows && prodAllows;
    }

    [Fact]
    public async Task MatrixMatchesExpectedDecisions()
    {
        using var http = new HttpClient { BaseAddress = fixture.OpaUri };
        var client = OpaPolicy.Decide(http);
        await AssertMatrixAsync(client, "OPA");
    }

    [Fact]
    public async Task CedarMatrixMatchesExpectedDecisions()
    {
        using var http = new HttpClient { BaseAddress = fixture.CedarUri };
        var client = CedarPolicy.Decide(http);
        await AssertMatrixAsync(client, "Cedar");
    }

    private async Task AssertMatrixAsync(DecidePolicy client, string engineName)
    {
        var table = new StringBuilder($"[{engineName}] user | resource tenant | env | action | decision | reasons\n");
        var mismatches = new List<string>();

        foreach (var (user, roles, tenant) in Users)
        {
            foreach (var resourceTenant in Tenants)
            {
                foreach (var environment in Environments)
                {
                    foreach (var action in Actions)
                    {
                        var input = new PolicyInput(
                            new PolicySubject(user, roles, tenant), environment, action, new PolicyResource("document", resourceTenant, "d1"));
                        var decision = await client(input, TestContext.Current.CancellationToken);
                        var line = $"{user} | {resourceTenant} | {environment} | {action} | {(decision.Allow ? "ALLOW" : "DENY")} | {string.Join("; ", decision.Reasons)}";
                        table.AppendLine(line);
                        if (decision.Allow != Expected(roles, tenant, resourceTenant, environment, action))
                        {
                            mismatches.Add(line);
                        }
                    }
                }
            }
        }

        output.WriteLine(table.ToString());
        mismatches.Should().BeEmpty();
    }
}
