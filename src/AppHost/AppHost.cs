using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var policiesPath = Path.Combine(repoRoot, "policies");
var infraPath = Path.Combine(repoRoot, "infra");

const string KeycloakEndpoint = "oidc";
const string BundlesVolume = "opa-bundles";

var keycloak = builder.AddKeycloak("keycloak")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: KeycloakEndpoint)
    .WithRealmImport("./keycloak");

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

builder.AddProject<Projects.Api>("api")
    .WithEnvironment("Auth__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint(KeycloakEndpoint)}/realms/opa-demo"))
    .WithEnvironment("Auth__Audience", "demo-api")
    .WithEnvironment("Opa__BaseAddress", opa.GetEndpoint("http"))
    .WithHttpHealthCheck("/buildinfo")
    .WaitFor(opa)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
