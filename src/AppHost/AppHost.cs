using System;
using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using CedarPolicyTool;
using Core;

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var policiesPath = Path.Combine(repoRoot, "policies");
var infraPath = Path.Combine(repoRoot, "infra");

var policyEngine = builder.Configuration["POLICY_ENGINE"] ?? "opa";
if (!policyEngine.Equals("opa", StringComparison.OrdinalIgnoreCase)
    && !policyEngine.Equals("cedar", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"POLICY_ENGINE must be 'opa' or 'cedar', got '{policyEngine}'.");
}

const string KeycloakEndpoint = "oidc";
const string BundlesVolume = "opa-bundles";

var keycloak = builder.AddKeycloak("keycloak")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: KeycloakEndpoint)
    .WithRealmImport("./keycloak");

keycloak.WithUrls(context =>
{
    var endpoint = context.GetEndpoint("http");
    context.Urls.Add(new ResourceUrlAnnotation
    {
        Url = $"{endpoint?.Url}/realms/opa-demo/account",
        DisplayText = "Account Login (opa-demo)"
    });
});

// Watches policies/, runs `opa test`, and publishes bundle.tar.gz to the shared volume.
var policyBuilder = builder.AddDockerfile("policy-builder", Path.Combine(infraPath, "policy-builder"))
    .WithBindMount(policiesPath, "/policies", isReadOnly: true)
    .WithVolume(BundlesVolume, "/bundles");

var bundleServer = builder.AddContainer("bundle-server", "nginx", "latest")
    .WithVolume(BundlesVolume, "/usr/share/nginx/html", isReadOnly: true)
    .WithBindMount(Path.Combine(infraPath, "bundle-server", "default.conf"), "/etc/nginx/conf.d/default.conf", isReadOnly: true)
    .WithHttpEndpoint(targetPort: 80)
    .WaitFor(policyBuilder);

var opa = builder.AddContainer("opa", "openpolicyagent/opa", "latest")
    .WithArgs("run", "--server", "--addr=0.0.0.0:8181", "--config-file=/config/opa-config.yaml")
    .WithBindMount(Path.Combine(infraPath, "opa", "opa-config.yaml"), "/config/opa-config.yaml", isReadOnly: true)
    .WithHttpEndpoint(port: 8181, targetPort: 8181, name: "http")
    .WithHttpHealthCheck("/health?bundles")
    .WaitFor(bundleServer);

// policies/authz.cedar is the source of truth; the agent's JSON wire format is generated from it on every start.
var cedarPoliciesJson = Path.Combine(infraPath, "cedar", "policies.json");
CedarPolicyGeneration.Generate(Path.Combine(policiesPath, "authz.cedar"), cedarPoliciesJson);

var cedar = builder.AddContainer("cedar", "permitio/cedar-agent", "latest")
    .WithEnvironment("CEDAR_AGENT_ADDR", "0.0.0.0")
    .WithEnvironment("CEDAR_AGENT_PORT", "8180")
    .WithEnvironment("CEDAR_AGENT_POLICIES", "/config/policies.json")
    .WithBindMount(cedarPoliciesJson, "/config/policies.json", isReadOnly: true)
    .WithHttpEndpoint(port: 8180, targetPort: 8180, name: "http");

var cosmos = builder.AddAzureCosmosDB(ConnectionNames.Cosmos)
    .RunAsEmulator();

var rabbitmq = builder.AddRabbitMQ(ConnectionNames.RabbitMq)
    .WithManagementPlugin();

var valkey = builder.AddValkey(ConnectionNames.Valkey);

builder.AddProject<Projects.Worker>("worker")
    .WithReference(cosmos)
    .WithReference(rabbitmq)
    .WithReference(valkey)
    .WaitFor(cosmos)
    .WaitFor(rabbitmq)
    .WaitFor(valkey);

builder.AddProject<Projects.Api>("api")
    .WithEnvironment("Auth__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint(KeycloakEndpoint)}/realms/opa-demo"))
    .WithEnvironment("Auth__Audience", "demo-api")
    .WithEnvironment("POLICY_ENGINE", policyEngine)
    .WithEnvironment("Opa__BaseAddress", opa.GetEndpoint("http"))
    .WithEnvironment("Cedar__BaseAddress", cedar.GetEndpoint("http"))
    .WithReference(cosmos)
    .WithReference(valkey)
    .WithHttpHealthCheck("/buildinfo")
    .WaitFor(cosmos)
    .WaitFor(valkey)
    .WaitFor(opa)
    .WaitFor(cedar)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
