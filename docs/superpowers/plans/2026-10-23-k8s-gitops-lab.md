# Kubernetes + Argo CD Learning Lab Implementation Plan

> **Status: complete.** Merged to `main` (PR #3, Argo tracks `main` since bcad2a9). Kept as a record of how the lab was built; the checkboxes below were not ticked as work progressed. Current instructions are in `deploy/README.md`.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deploy this repo's authorization demo (Api with OPA and Cedar sidecars, Worker, Keycloak, Cosmos, RabbitMQ, Valkey) to a local kind cluster managed by Argo CD, with Gatekeeper guardrails and a dev → staging → prod promotion flow.

**Architecture:** A `deploy/` folder holds bootstrap scripts, build scripts and every manifest. Argo CD (app of apps) syncs platform (Envoy Gateway, Gatekeeper), shared `infra` services and three app namespaces from Git. Images and policy artifacts are built on the laptop, tagged with the git short SHA and pushed to a local registry at `localhost:5001`.

**Tech Stack:** kind, Argo CD + KSOPS, SOPS + age, Envoy Gateway (Gateway API), OPA Gatekeeper + gator, Kustomize, Helm, oras, .NET 10 SDK container publishing, xunit v3 on Microsoft.Testing.Platform.

**Spec:** `docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md`

## Global Constraints

- Aspire must behave exactly as today: every new setting defaults to current behaviour when unset.
- Build settings are strict (`TreatWarningsAsErrors`, `AnalysisMode=All`). Do not add any warning suppression (`#pragma`, `NoWarn`, `SuppressMessage`, `NuGetAuditSuppress`) without asking the user first.
- A seam with one operation is a named delegate, not an interface (the repo's existing pattern: `DecidePolicy`, `WriteOutboxEvent`).
- Commit messages carry no AI attribution or co-author lines.
- No `:latest` and no untagged image anywhere under `deploy/`. Pin each third-party image and chart to its current stable release at implementation time.
- App images and policy artifacts are tagged with the git short SHA, and only from a clean tree for their inputs.
- Hostnames: `api.dev.localtest.me`, `api.staging.localtest.me`, `api.prod.localtest.me`, `keycloak.localtest.me`, `dashboard.localtest.me`.
- Namespaces: `argocd`, `gatekeeper-system`, `envoy-gateway-system`, `infra`, `app-dev`, `app-staging`, `app-prod`. Each carries `lab.dev/tier: platform | infra | app`.
- Per-env isolation: RabbitMQ vhost `dev`/`staging`/`prod`, Valkey DB `0`/`1`/`2`, Cosmos database `policy-demo-dev`/`policy-demo-staging`/`policy-demo-prod`.
- Per-env engine: dev `opa`, staging `cedar`, prod `opa`. Allowed environments: dev `[dev, staging, prod]`, staging `[staging, prod]`, prod `[prod]`. Each deployment's default environment is its own.
- Argo tracks `targetRevision: k8s` until the PR merges, then `main`.
- Never run `aspire run` while the kind cluster is up (ports 8080, 8181, 8180 and memory).
- Explain each Kubernetes concept briefly as it is introduced (commit messages and short comments in manifests); this is a learning project.

## Review Focus

1. **Invalid deployment environment config** (default outside the allowlist, unknown name in the allowlist): the Api must refuse to start, not fail per request. Pinned in Task 8.
2. **Header present but empty** (`X-Environment:`): treated as missing, so it uses the default. Pinned in Task 8.
3. **Building artifacts from a dirty tree**: the SHA tag would lie about the content. The build scripts refuse unless `ALLOW_DIRTY=1`, which appends `-dirty`. Pinned in Tasks 11 and 12.
4. **CedarPolicyTool with a missing input file or wrong arguments**: non-zero exit with a message, and no output file written. Pinned in Task 10.
5. **Re-running bootstrap on an existing cluster or registry**: it must be a no-op, not an error. Pinned in Task 2.

---

## Phase 0: Cosmos spike

### Task 1: vNext emulator spike (throwaway)

**Files:**
- Create (throwaway, never committed): a scratch directory outside the repo
- Modify: `docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md` (decision 13 row: record the verdict)

**Interfaces:**
- Produces: the verdict "vnext" or "classic", consumed by Task 7.

- [ ] **Step 1:** Run `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview` in Docker with `--protocol http` on port 8081.
- [ ] **Step 2:** Point the existing stack at it without Aspire: run `src/Worker` and `src/Api` with `ConnectionStrings__cosmos=AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==` plus RabbitMQ and Valkey from `docker run`, then create, update and delete a document through the Api.
- [ ] **Step 3:** Pass criteria, all required: `CosmosInitializerService` completes; the create returns 201 (transactional batch works); the audit entry for the create appears via `GET /audit` within 10 s (change feed processor with the `leases` container works). Record failures verbatim.
- [ ] **Step 4:** Write the verdict and the reason (one line) into the spec's decision 13 row. Delete the spike files and containers.
- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md
git commit -m "docs: record Cosmos emulator spike verdict"
```

## Phase 1: Cluster and registry

### Task 2: kind cluster, local registry, CoreDNS rewrite

**Files:**
- Create: `deploy/bootstrap/check-tools.sh`, `deploy/bootstrap/kind-config.yaml`, `deploy/bootstrap/registry.sh`, `deploy/bootstrap/coredns-patch.yaml`, `deploy/bootstrap/cluster.sh`, `deploy/README.md`

**Interfaces:**
- Produces: cluster `kind-lab` (context `kind-lab`); registry container `kind-registry` reachable as `localhost:5001` from laptop and nodes; CoreDNS rewrite `keycloak.localtest.me` → `envoy-lab.envoy-gateway-system.svc.cluster.local` (the Service Task 4 creates); host ports 80/443 → node ports 30080/30443.

- [ ] **Step 1:** `check-tools.sh` verifies `docker kind kubectl helm kustomize sops age argocd oras gator dotnet` are on PATH and prints the install hint for each missing one (`brew install kind helm kustomize sops age argocd oras gator`; keep gator on the same minor version as the pinned Gatekeeper chart).
- [ ] **Step 2:** `kind-config.yaml`: 1 control-plane + 2 workers; on the control-plane node `extraPortMappings` 30080→80 and 30443→443; `containerdConfigPatches` adding `/etc/containerd/certs.d` as the registry config path (kind's documented local-registry pattern).
- [ ] **Step 3:** `registry.sh`: start `registry:2` (pinned tag) as `kind-registry` on `127.0.0.1:5001` unless it already runs; connect it to the `kind` network unless already connected; on each node write `/etc/containerd/certs.d/localhost:5001/hosts.toml` pointing at `http://kind-registry:5000`; apply the `local-registry-hosting` ConfigMap in `kube-public`.
- [ ] **Step 4:** `coredns-patch.yaml`: the full CoreDNS Corefile ConfigMap with `rewrite name keycloak.localtest.me envoy-lab.envoy-gateway-system.svc.cluster.local` before the `kubernetes` plugin.
- [ ] **Step 5:** `cluster.sh`: runs check-tools, creates the cluster unless `kind get clusters` lists `lab`, runs registry.sh, applies coredns-patch.yaml and restarts the `coredns` Deployment. Set `set -euo pipefail`.
- [ ] **Step 6: Verify**

Run: `deploy/bootstrap/cluster.sh && deploy/bootstrap/cluster.sh` (twice: Review Focus 5)
Expected: second run exits 0 and changes nothing; `kubectl get nodes` shows 3 `Ready`.

Run: `docker pull busybox:1.36 && docker tag busybox:1.36 localhost:5001/busybox:1.36 && docker push localhost:5001/busybox:1.36 && kubectl run pull-test --image=localhost:5001/busybox:1.36 --restart=Never -- true && kubectl wait --for=jsonpath='{.status.phase}'=Succeeded pod/pull-test --timeout=60s`
Expected: `pod/pull-test condition met`. Delete the pod.

- [ ] **Step 7:** `deploy/README.md`: prerequisites, "bootstrap from nothing" command sequence (grows in later tasks), the never-alongside-Aspire warning.
- [ ] **Step 8: Commit** (`feat(deploy): kind cluster with local registry and CoreDNS rewrite`)

## Phase 2: Argo CD, secrets, gateway

### Task 3: Argo CD with KSOPS and the app of apps

**Files:**
- Create: `.sops.yaml`, `deploy/bootstrap/argocd-install.sh`, `deploy/platform/argocd/values.yaml`, `deploy/argocd/root-app.yaml`, `deploy/argocd/apps/namespaces.yaml`, `deploy/platform/namespaces/kustomization.yaml`, `deploy/platform/namespaces/namespaces.yaml`, `deploy/infra/secrets/smoke.sops.yaml`, `deploy/infra/secrets/kustomization.yaml`, `deploy/infra/secrets/secret-generator.yaml`, `deploy/argocd/apps/infra-secrets.yaml`

**Interfaces:**
- Produces: Application naming `<component>` or `api-<env>`; every Application uses `project: default`, `repoURL: https://github.com/bmcdavid/open-policy-demo.git`, `targetRevision: k8s`; KSOPS generator pattern (`secret-generator.yaml` listing `*.sops.yaml` files) reused by Tasks 5-7 and 13.

- [ ] **Step 1:** Generate an age key at `~/.config/sops/age/keys.txt` if absent. `.sops.yaml` rule: `path_regex: deploy/.*/secrets/.*\.sops\.yaml$`, `encrypted_regex: ^(data|stringData)$`, `age: <public key>`.
- [ ] **Step 2:** `values.yaml` for the `argo-cd` Helm chart (pinned): kustomize build options `--enable-alpha-plugins --enable-exec`; a `repoServer` init container that installs the pinned KSOPS binary; mount of Secret `sops-age` at `/home/argocd/.config/sops/age/keys.txt`; `XDG_CONFIG_HOME` set accordingly. Use the KSOPS README's Argo CD Helm values as the template.
- [ ] **Step 3:** `argocd-install.sh`: `helm upgrade --install` the chart into `argocd` with values.yaml; create Secret `sops-age` from the local key file (`kubectl create secret ... --dry-run=client -o yaml | kubectl apply -f -`, idempotent); wait for the repo server; apply `root-app.yaml`.
- [ ] **Step 4:** `root-app.yaml`: Application `root` watching `deploy/argocd/apps`, automated sync with prune and self-heal.
- [ ] **Step 5:** `namespaces.yaml` Application (wave `-4`) syncing `deploy/platform/namespaces`, which declares all seven namespaces with their `lab.dev/tier` labels.
- [ ] **Step 6:** `smoke.sops.yaml`: Secret `sops-smoke` in `infra` with `stringData.value: decrypted`, encrypted with `sops -e -i`. `infra-secrets` Application (wave `-1`) syncs `deploy/infra/secrets`.
- [ ] **Step 7: Push and verify**

Run: `git push -u origin k8s && deploy/bootstrap/argocd-install.sh && argocd app wait root namespaces infra-secrets --health --sync --timeout 300`
Expected: all three `Synced` and `Healthy`; `kubectl -n infra get secret sops-smoke -o jsonpath='{.data.value}' | base64 -d` prints `decrypted`.

Run: `kubectl delete ns app-dev && sleep 200 && kubectl get ns app-dev`
Expected: the namespace exists again (self-heal).

- [ ] **Step 8:** Append the Argo steps and the UI login (`argocd admin initial-password -n argocd`, port-forward 8443) to `deploy/README.md`.
- [ ] **Step 9: Commit** (`feat(deploy): Argo CD with KSOPS and app of apps`)

### Task 4: Envoy Gateway and the shared Gateway

**Files:**
- Create: `deploy/argocd/apps/envoy-gateway.yaml`, `deploy/argocd/apps/gateway.yaml`, `deploy/platform/gateway/kustomization.yaml`, `deploy/platform/gateway/envoy-proxy.yaml`, `deploy/platform/gateway/gateway-class.yaml`, `deploy/platform/gateway/gateway.yaml`

**Interfaces:**
- Consumes: namespaces from Task 3; CoreDNS target name from Task 2.
- Produces: `Gateway` `lab` in `envoy-gateway-system` with HTTP listener on port 80 for `*.localtest.me`, `allowedRoutes.namespaces.from: Selector` matching `lab.dev/tier in (infra, app)`. Routes in later tasks attach with `parentRefs: [{name: lab, namespace: envoy-gateway-system}]`.

- [ ] **Step 1:** `envoy-gateway.yaml`: Application (wave `-3`) for the `gateway-helm` OCI chart (pinned) with `ServerSideApply=true`.
- [ ] **Step 2:** `envoy-proxy.yaml`: `EnvoyProxy` `lab` setting `provider.kubernetes.envoyService` to `name: envoy-lab`, `type: NodePort`, and patching node ports 30080 (http) and 30443 (https). A fixed Service name is what lets the CoreDNS rewrite and the kind port mapping find it.
- [ ] **Step 3:** `gateway-class.yaml` (`lab`, `parametersRef` → the EnvoyProxy) and `gateway.yaml` as in Interfaces. `gateway.yaml` Application at wave `-2`.
- [ ] **Step 4: Verify**

Run: `argocd app wait envoy-gateway gateway --health --timeout 300 && kubectl -n envoy-gateway-system get gateway lab -o jsonpath='{.status.conditions[?(@.type=="Programmed")].status}'`
Expected: `True`.

Run: `kubectl run dns-test --image=busybox:1.36 --restart=Never --rm -it -- nslookup keycloak.localtest.me`
Expected: the ClusterIP of Service `envoy-lab`.

Run: `curl -s -o /dev/null -w '%{http_code}' http://nothing.localtest.me/`
Expected: `404` from Envoy (the Gateway answers on host port 80).

- [ ] **Step 5: Commit** (`feat(deploy): Envoy Gateway with shared lab Gateway`)

## Phase 3: Infra

### Task 5: Keycloak and the Aspire Dashboard

**Files:**
- Create: `deploy/argocd/apps/infra.yaml`, `deploy/infra/kustomization.yaml`, `deploy/infra/keycloak/{kustomization.yaml,deployment.yaml,service.yaml,httproute.yaml}`, `deploy/infra/dashboard/{kustomization.yaml,deployment.yaml,service.yaml,httproute.yaml}`, `deploy/infra/secrets/keycloak-admin.sops.yaml`
- Modify: `deploy/infra/secrets/secret-generator.yaml`

**Interfaces:**
- Consumes: Gateway `lab` (Task 4); KSOPS pattern (Task 3).
- Produces: issuer `http://keycloak.localtest.me/realms/opa-demo`; Service `keycloak.infra:8080`; OTLP endpoint `http://aspire-dashboard.infra:18889` (gRPC); `infra` Application (wave `-1`) that later infra tasks add to via `deploy/infra/kustomization.yaml`.

- [ ] **Step 1:** `deploy/infra/keycloak/kustomization.yaml`: `configMapGenerator` `keycloak-realm` from `../../../src/AppHost/keycloak/opa-demo-realm.json`. That path is outside the kustomization root, so add `--load-restrictor LoadRestrictionsNone` to the Argo kustomize build options in `deploy/platform/argocd/values.yaml` and say why in a comment. (One realm file serves Aspire and k8s.)
- [ ] **Step 2:** Keycloak Deployment (pinned `quay.io/keycloak/keycloak`): `start-dev --import-realm`, `KC_HOSTNAME=http://keycloak.localtest.me`, `KC_HTTP_ENABLED=true`, admin credentials from Secret `keycloak-admin`, realm mounted at `/opt/keycloak/data/import`, readiness on `/health/ready` with `KC_HEALTH_ENABLED=true`, requests and limits set, labels `app.kubernetes.io/name: keycloak`, `owner: lab`. HTTPRoute for `keycloak.localtest.me`.
- [ ] **Step 3:** Dashboard Deployment (pinned `mcr.microsoft.com/dotnet/aspire-dashboard`): `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true`, ports 18888 (UI) and 18889 (OTLP gRPC). Service `aspire-dashboard`; HTTPRoute for `dashboard.localtest.me` → 18888.
- [ ] **Step 4: Verify**

Run: `curl -s -d grant_type=password -d client_id=demo-api -d username=reader-acme -d 'password=Passw0rd!' http://keycloak.localtest.me/realms/opa-demo/protocol/openid-connect/token | jq -r .access_token | cut -d. -f2 | base64 -d 2>/dev/null | jq -r .iss`
Expected: `http://keycloak.localtest.me/realms/opa-demo`.

Run: `curl -s -o /dev/null -w '%{http_code}' http://dashboard.localtest.me/`
Expected: `200`.

- [ ] **Step 5: Commit** (`feat(deploy): Keycloak and Aspire Dashboard in infra`)

### Task 6: RabbitMQ and Valkey

**Files:**
- Create: `deploy/infra/rabbitmq/{kustomization.yaml,statefulset.yaml,service.yaml,definitions.json}`, `deploy/infra/valkey/{kustomization.yaml,deployment.yaml,service.yaml}`, `deploy/infra/secrets/rabbitmq.sops.yaml`
- Modify: `deploy/infra/kustomization.yaml`, `deploy/infra/secrets/secret-generator.yaml`

**Interfaces:**
- Produces: `amqp://<user>:<pass>@rabbitmq.infra:5672/<vhost>` with users `app-dev`, `app-staging`, `app-prod`, each limited to its own vhost; `valkey.infra:6379`.

- [ ] **Step 1:** `definitions.json` declares vhosts `dev`, `staging`, `prod`, the three users with full permissions on their own vhost only, and an `admin` user. Passwords are not in this file: the `rabbitmq` SOPS Secret holds a complete `definitions.json` (users with `password_hash`) mounted at `/etc/rabbitmq/definitions.json`, with `load_definitions` set in `rabbitmq.conf`. Keep the committed `definitions.json` as the unhashed shape reference only if it adds clarity; otherwise drop it.
- [ ] **Step 2:** StatefulSet (pinned `rabbitmq:<ver>-management`), one replica, `volumeClaimTemplates` 1Gi, readiness `rabbitmq-diagnostics -q check_port_connectivity`. Headless Service plus ClusterIP Service `rabbitmq`.
- [ ] **Step 3:** Valkey Deployment (pinned `valkey/valkey`), no persistence, readiness `valkey-cli ping`.
- [ ] **Step 4: Verify**

Run: `kubectl -n infra exec rabbitmq-0 -- rabbitmqctl list_permissions -p prod`
Expected: only `app-prod` and `admin`.

Run: `kubectl -n infra exec deploy/valkey -- valkey-cli -n 2 ping`
Expected: `PONG`.

- [ ] **Step 5: Commit** (`feat(deploy): RabbitMQ with per-env vhosts and Valkey`)

### Task 7: Cosmos emulator

**Files:**
- Create: `deploy/infra/cosmos/{kustomization.yaml,deployment.yaml,service.yaml}`, `deploy/infra/secrets/cosmos.sops.yaml`
- Modify: `deploy/infra/kustomization.yaml`

**Interfaces:**
- Consumes: Task 1 verdict.
- Produces: `cosmos.infra:8081`; connection string held in Secret `cosmos` key `connection-string`, reused by the app overlays (Task 13).

- [ ] **Step 1 (vnext):** pinned `vnext-preview` image, `--protocol http`, connection string `AccountEndpoint=http://cosmos.infra:8081/;AccountKey=<emulator key>`.
- [ ] **Step 1 (classic):** pinned `azure-cosmos-emulator` image. Give the Service a fixed `clusterIP` in the service CIDR (e.g. `10.96.100.10`) and set `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE` to it, so the endpoints the emulator advertises are reachable from other pods. Connection string `AccountEndpoint=https://cosmos.infra:8081/;AccountKey=<emulator key>;DisableServerCertificateValidation=True`. Memory limit 3Gi. Raise Docker Desktop to 14-16 GB first.
- [ ] **Step 2:** `startupProbe` (TCP 8081, `failureThreshold` × `periodSeconds` ≥ 300 s), then readiness. `CosmosInitializerService` already retries 20 × 3 s and then fails the process; a pod restart after that is acceptable, so no code change.
- [ ] **Step 3: Verify**

Run: `kubectl -n infra wait --for=condition=Ready pod -l app.kubernetes.io/name=cosmos --timeout=600s`
Expected: `condition met`.

- [ ] **Step 4: Commit** (`feat(deploy): Cosmos emulator in infra`)

## Phase 4: Code changes and policy artifacts

These tasks touch only `src/`, `tests/` and `deploy/build/` and can run before Phases 1-3 if convenient.

### Task 8: Deployment environment allowlist

**Files:**
- Create: `src/Core/Features/Authorization/DeploymentEnvironment.cs`, `tests/Tests/Core/DeploymentEnvironmentTests.cs`
- Modify: `src/Api/Features/Authorization/DocumentAccess.cs:17-60`, `src/Api/Features/Authorization/ConfigureServicesAndEndpoints.cs:63`, `tests/Tests/Api/DocumentAccessTests.cs:19-37`

**Interfaces:**
- Produces (in `Core.Features.Authorization`):
  - `public readonly record struct EnvironmentResolution(string? Environment, string? Error)`: exactly one is non-null.
  - `public delegate EnvironmentResolution ResolveEnvironment(string? headerValue);`
  - `public static class DeploymentEnvironment` with `public static ResolveEnvironment Create(IReadOnlyCollection<string>? allowed, string? defaultEnvironment)` and `public static ResolveEnvironment FromConfiguration(IConfiguration configuration)` reading `Deployment:AllowedEnvironments` (array) and `Deployment:DefaultEnvironment`.
  - Error strings: unknown or missing → `X-Environment must be one of dev, staging, prod` (today's text); known but not allowed → `environment not allowed in this deployment`.
- `DocumentAccess` constructor becomes `(IDocumentStore store, DecidePolicy decidePolicy, WriteOutboxEvent writeOutbox, ResolveEnvironment resolveEnvironment)`.

Config keys live under `Deployment:` rather than `Environment:` because `ASPNETCORE_ENVIRONMENT` already occupies the `environment` configuration key.

- [ ] **Step 1: Write the failing tests** in `DeploymentEnvironmentTests`:

```csharp
[Fact]
public void UnsetBehavesAsToday()
{
    var resolve = DeploymentEnvironment.Create(null, null);
    resolve("prod").Environment.Should().Be("prod");
    resolve(null).Error.Should().Be("X-Environment must be one of dev, staging, prod");
    resolve("qa").Error.Should().Be("X-Environment must be one of dev, staging, prod");
}

[Fact]
public void KnownValueOutsideTheAllowlistIsRejected() =>
    DeploymentEnvironment.Create(["prod"], "prod")("dev").Error.Should().Be("environment not allowed in this deployment");

[Theory]
[InlineData(null)]
[InlineData("")]
public void MissingOrEmptyHeaderUsesTheDefault(string? header) =>
    DeploymentEnvironment.Create(["staging", "prod"], "staging")(header).Environment.Should().Be("staging");

[Fact]
public void ExplicitAllowedValueWinsOverTheDefault() =>
    DeploymentEnvironment.Create(["dev", "staging", "prod"], "dev")("prod").Environment.Should().Be("prod");

[Theory]
[InlineData(new[] { "qa" }, null)]
[InlineData(new[] { "prod" }, "dev")]
[InlineData(new[] { "Prod" }, null)]
public void InvalidConfigurationThrowsAtCreation(string[] allowed, string? defaultEnvironment) =>
    FluentActions.Invoking(() => DeploymentEnvironment.Create(allowed, defaultEnvironment)).Should().Throw<InvalidOperationException>();

[Fact]
public void FromConfigurationReadsTheDeploymentSection()
{
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Deployment:AllowedEnvironments:0"] = "prod",
        ["Deployment:DefaultEnvironment"] = "prod",
    }).Build();
    var resolve = DeploymentEnvironment.FromConfiguration(configuration);
    resolve(null).Environment.Should().Be("prod");
    resolve("staging").Error.Should().Be("environment not allowed in this deployment");
}
```

Add to `DocumentAccessTests`: give `Fixture` a `ResolveEnvironment Environment { get; set; } = DeploymentEnvironment.Create(null, null);` passed to `DocumentAccess`, and

```csharp
[Fact]
public async Task EnvironmentOutsideTheAllowlistIs400BeforeTheDocumentIsLoadedOrPolicyIsAsked()
// f.Environment = DeploymentEnvironment.Create(["prod"], "prod"); header "dev" (the Context() default);
// assert both Existing and InTenant return 400, f.Policy.Inputs and f.Events empty (same shape as MissingEnvironmentIs400...).
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Tests --filter-class "Tests.Core.DeploymentEnvironmentTests" --filter-class "Tests.Api.DocumentAccessTests"`
Expected: build failure, `DeploymentEnvironment` not found.

- [ ] **Step 3: Implement** `DeploymentEnvironment` (use `DemoEnvironments.TryParse` for "known"; an empty or null allowlist means all three; treat `""` as missing; validate at `Create` with an `InvalidOperationException` naming the bad value). Replace both `DemoEnvironments.TryParse` checks in `DocumentAccess` with `resolveEnvironment(...)`, returning `Results.BadRequest(new { error = resolution.Error })`. Register in `ConfigureAuthorization` with `builder.Services.AddSingleton(DeploymentEnvironment.FromConfiguration(builder.Configuration));` so bad config fails at startup.
- [ ] **Step 4: Run all tests**

Run: `dotnet test --project tests/Tests --filter-not-namespace "Tests.E2E"`
Expected: PASS, including the unchanged `MissingEnvironmentIs400BeforeTheDocumentIsLoadedOrPolicyIsAsked`.

- [ ] **Step 5: Commit** (`feat(api): per-deployment environment allowlist and default`)

### Task 9: Configurable Cosmos database and HTTPS metadata flag

**Files:**
- Modify: `src/Core/Features/Cosmos/CosmosNames.cs:5`, `src/Core/Hosting/ServiceDefaultsExtensions.cs:37-45`, `src/Core/Features/Cosmos/CosmosSetup.cs:18-30`, `src/Core/Features/Cosmos/CosmosDocumentStore.cs:12-16`, `src/Core/Features/Cosmos/CosmosOutbox.cs:25-28`, `src/Core/Features/Audit/CosmosAudit.cs:11-32`, `src/Core/Features/Cosmos/CosmosInitializerService.cs:8`, `src/Worker/Features/Relay/OutboxRelayService.cs:11-17`, `src/Api/Features/Documents/ConfigureServicesAndEndpoints.cs:41-55`, `src/Worker/CompositionRoot.cs:40-48`, `src/Api/Features/Authorization/ConfigureServicesAndEndpoints.cs:25`
- Test: `tests/Tests/Core/ServiceDefaultsTests.cs`, `tests/Tests/Api/WhoAmITests.cs` (or a new `tests/Tests/Api/AuthOptionsTests.cs`)

**Interfaces:**
- `CosmosNames.Database` is renamed `CosmosNames.DefaultDatabase` (value `policy-demo`).
- `AddCosmosClient` also registers `Microsoft.Azure.Cosmos.Database` as a singleton: `client.GetDatabase(configuration["Cosmos:Database"] ?? CosmosNames.DefaultDatabase)`.
- Every Cosmos consumer takes `Database` instead of `CosmosClient`: `new CosmosDocumentStore(Database database, TimeProvider? time = null)`, `CosmosOutbox.Write(Database database)`, `CosmosAudit.Upsert(Database database)`, `CosmosAudit.Query(Database database)`, `CosmosSetup.EnsureCreatedAsync(Database database, CancellationToken)` (creates via `database.Client.CreateDatabaseIfNotExistsAsync(database.Id)`), `CosmosInitializerService(Database database, ILogger<...>)`, `OutboxRelayService(Database database, PublishEvent, ILogger<...>)`.
- `Auth:RequireHttpsMetadata` (bool) overrides the `!IsDevelopment()` default.

- [ ] **Step 1: Write the failing tests**
  - `ServiceDefaultsTests.CosmosDatabaseDefaultsToPolicyDemo` and `CosmosDatabaseFollowsConfiguration`: build a host with `ConnectionStrings:cosmos=AccountEndpoint=https://localhost:8081/;AccountKey=<emulator key>` (no connection is made), call `AddCosmosClient()`, resolve `Database`, assert `.Id` is `policy-demo` / `policy-demo-dev` when `Cosmos:Database=policy-demo-dev`.
  - `AuthOptionsTests.RequireHttpsMetadataFollowsConfiguration`: via `ApiFactory` with `UseSetting("Auth:RequireHttpsMetadata", "false")` in a non-Development environment, resolve `IOptionsMonitor<JwtBearerOptions>.Get(JwtBearerDefaults.AuthenticationScheme).RequireHttpsMetadata` and assert `false`; without the setting in `Production`, assert `true`.
- [ ] **Step 2:** Run them. Expected: FAIL (no `Database` registration; flag ignored).
- [ ] **Step 3:** Implement per Interfaces. The flag: `o.RequireHttpsMetadata = builder.Configuration.GetValue<bool?>("Auth:RequireHttpsMetadata") ?? !builder.Environment.IsDevelopment();`
- [ ] **Step 4:** Run the full non-E2E suite, then the Aspire E2E suite once (`dotnet test --project tests/Tests` with Docker running). Expected: PASS; Aspire still uses database `policy-demo`.
- [ ] **Step 5: Commit** (`feat: configurable Cosmos database and HTTPS metadata requirement`)

### Task 10: CedarPolicyTool

**Files:**
- Create: `src/CedarPolicyTool/CedarPolicyTool.csproj`, `src/CedarPolicyTool/Program.cs`, `src/CedarPolicyTool/Cli.cs`, `tests/Tests/Cedar/CedarPolicyToolCliTests.cs`
- Move (git mv): `src/AppHost/Cedar/{AgentPolicy,AgentPolicyJson,CedarLexer,CedarPolicyGeneration,CedarPolicySplitter}.cs` → `src/CedarPolicyTool/`, namespace `AppHost.Cedar` → `CedarPolicyTool`
- Modify: `src/AppHost/AppHost.cs:3`, `src/AppHost/AppHost.csproj`, `tests/Tests/Tests.csproj`, `tests/Tests/Cedar/*.cs` (usings), `tests/Tests/E2E/CedarPolicyLoadTests.cs` (using), `opa.slnx`

**Interfaces:**
- Produces: `internal static class Cli { public static int Run(IReadOnlyList<string> args, TextWriter error); }`. `generate <authz.cedar> <policies.json>` → 0. Invalid Cedar or missing input → 1, message on `error`, output file untouched. Anything else → 2 with usage `usage: CedarPolicyTool generate <authz.cedar> <policies.json>`.
- `Program.cs` is `return Cli.Run(args, Console.Error);`.
- Command used by Task 12: `dotnet run --project src/CedarPolicyTool -- generate policies/authz.cedar <out>/policies.json`.

- [ ] **Step 1:** `git mv` the five files and fix namespaces; create the csproj (`OutputType Exe`, `SkipBuildInfoAttribute true` since it does not reference Core); AppHost references it with `IsAspireProjectResource="false"` (same as Core); Tests references it; add to `opa.slnx`. Run the existing Cedar tests. Expected: PASS (pure move).
- [ ] **Step 2: Write the failing tests** in `CedarPolicyToolCliTests` (temp directory like `CedarPolicyGenerationTests`):
  - `GenerateWritesThePoliciesFile`: valid source → returns 0, output exists and parses as a JSON array with one element per `@id`.
  - `InvalidSourceReturnsOneWithLineNumbersAndWritesNothing`: error text contains `line `; output file does not exist.
  - `MissingInputReturnsOneAndWritesNothing`: error text contains the missing path.
  - `WrongArgumentsReturnTwoWithUsage` (`[]`, `["generate", "a"]`, `["build", "a", "b"]`): error text starts with `usage: CedarPolicyTool generate`.
- [ ] **Step 3:** Run. Expected: FAIL (`Cli` not found).
- [ ] **Step 4:** Implement `Cli.Run` on top of `CedarPolicyGeneration.Generate`, catching `InvalidOperationException` and `FileNotFoundException`/`DirectoryNotFoundException`.
- [ ] **Step 5:** Run the Cedar tests and `dotnet run --project src/CedarPolicyTool -- generate policies/authz.cedar /tmp/p.json; echo $?`. Expected: PASS; `0`.
- [ ] **Step 6: Commit** (`refactor: move Cedar policy generation into CedarPolicyTool`)

### Task 11: build-images.sh

**Files:**
- Create: `deploy/build/lib.sh`, `deploy/build/build-images.sh`

**Interfaces:**
- Produces: `lib.sh` function `artifact_tag <paths...>` printing the short SHA of `HEAD`, failing if any path has uncommitted changes unless `ALLOW_DIRTY=1` (then `<sha>-dirty`). Used by Task 12.
- Produces images `localhost:5001/api:<tag>` and `localhost:5001/worker:<tag>`; script prints the tag as its last line.

- [ ] **Step 1:** `build-images.sh`: `tag=$(artifact_tag src Directory.Build.props Directory.Packages.props)`; for `Api` and `Worker`: `GIT_HASH=$tag GIT_BRANCH=$(git branch --show-current) dotnet publish src/<Project> -c Release -t:PublishContainer -p:ContainerRegistry=localhost:5001 -p:ContainerRepository=<api|worker> -p:ContainerImageTag=$tag`.
- [ ] **Step 2: Verify**

Run: `deploy/build/build-images.sh && curl -s localhost:5001/v2/api/tags/list`
Expected: tags include the printed tag.

Run: `touch src/Api/dirty.txt && deploy/build/build-images.sh; echo $?; rm src/Api/dirty.txt`
Expected: non-zero with a message naming the dirty path (Review Focus 3).

- [ ] **Step 3: Commit** (`feat(deploy): build and push app images tagged by git SHA`)

### Task 12: build-policies.sh

**Files:**
- Create: `deploy/build/build-policies.sh`, `deploy/build/cedar-policies/Dockerfile`

**Interfaces:**
- Consumes: `artifact_tag` (Task 11); CedarPolicyTool command (Task 10).
- Produces: OCI artifact `localhost:5001/opa-bundle:<tag>` (single layer, media type `application/vnd.oci.image.layer.v1.tar+gzip`); image `localhost:5001/cedar-policies:<tag>` with `/config/policies.json`; and the pinned OPA image mirrored as `localhost:5001/opa:<opa-version>` (app pods may only pull from `localhost:5001`). Script prints the tag as its last line.
- Pods reach the registry as `http://kind-registry:5000` (CoreDNS forwards to Docker's DNS on the `kind` network). `localhost:5001` only works for image pulls, through the containerd mirror.

- [ ] **Step 1:** Script: `tag=$(artifact_tag policies src/CedarPolicyTool)`; `docker run --rm -v "$PWD/policies:/policies" openpolicyagent/opa:<pin> test /policies`; `opa build --bundle /policies --ignore '*_test.rego'` (same flags as `infra/policy-builder/watch.sh`) into a temp dir; `oras push --plain-http localhost:5001/opa-bundle:$tag bundle.tar.gz:application/vnd.oci.image.layer.v1.tar+gzip`; CedarPolicyTool into the build context; `docker build -t localhost:5001/cedar-policies:$tag deploy/build/cedar-policies` (`FROM permitio/cedar-agent:<pin>`, `COPY policies.json /config/policies.json`); `docker push`; `docker tag openpolicyagent/opa:<pin> localhost:5001/opa:<pin> && docker push localhost:5001/opa:<pin>` (skip if already present).
- [ ] **Step 2: Verify the gate**

Run: break one assertion in `policies/authz_test.rego`, commit it on a throwaway branch, run the script, `echo $?`, then delete the branch.
Expected: non-zero; `oras repo tags --plain-http localhost:5001/opa-bundle` has no new tag.

- [ ] **Step 3: Verify OPA loads the OCI bundle over plain HTTP** (spec check)

Run: `docker run --rm --network kind -v <tmp>/opa-config.yaml:/c.yaml openpolicyagent/opa:<pin> run --server --config-file=/c.yaml` with a config whose service is `url: http://kind-registry:5000`, `type: oci`, and bundle `resource: kind-registry:5000/opa-bundle:<tag>`; then `curl localhost:8181/health?bundles` from inside the container network.
Expected: `{}` with HTTP 200. If OPA refuses plain HTTP, switch to the spec's fallback (an `opa-bundle:<tag>` nginx image serving `bundle.tar.gz`, run as a third native sidecar on `127.0.0.1:8282`) and record which path was taken in the spec's Api pod section.

- [ ] **Step 4: Commit** (`feat(deploy): build and push OPA bundle and Cedar policy image`)

## Phase 5: App in dev

### Task 13: Api and Worker in app-dev

**Files:**
- Create: `deploy/apps/api/base/{kustomization.yaml,api-deployment.yaml,worker-deployment.yaml,service.yaml,httproute.yaml,serviceaccount.yaml}`, `deploy/apps/api/overlays/dev/{kustomization.yaml,opa-config.yaml,secrets/connections.sops.yaml,secrets/secret-generator.yaml}`, `deploy/argocd/apps/api-dev.yaml`

**Interfaces:**
- Consumes: images and tags from Tasks 11-12; Secret `cosmos` pattern (Task 7); hostnames and per-env values from Global Constraints.
- Produces: the base and overlay shape Task 16 copies for staging and prod. Overlay sets: `images:` tags for `api`, `worker`, `cedar-policies`; `opa-config.yaml` bundle tag; `POLICY_ENGINE`; `Deployment__AllowedEnvironments__N`; `Deployment__DefaultEnvironment`; `Cosmos__Database`; HTTPRoute hostname; replicas.

- [ ] **Step 1:** Base Api Deployment exactly as the spec's Api pod section, plus env `Auth__Authority=http://keycloak.localtest.me/realms/opa-demo`, `Auth__Audience=demo-api`, `Auth__RequireHttpsMetadata=false`, `ASPNETCORE_ENVIRONMENT=Production`, `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard.infra:18889`, `OTEL_SERVICE_NAME=api`, and `ConnectionStrings__cosmos|rabbitmq|valkey` from Secret `connections`. Worker Deployment: same Secret, `Cosmos__Database`, `OTEL_SERVICE_NAME=worker`, no sidecars, no Service. Requests and limits on every container (including the two init containers) and labels `app.kubernetes.io/name`, `owner: lab` on every pod template.
- [ ] **Step 2:** dev overlay: values from Global Constraints (`Allowed = dev, staging, prod`, default `dev`, engine `opa`, database `policy-demo-dev`, vhost `dev`, Valkey `valkey.infra:6379,defaultDatabase=0`). `opa-config.yaml` via `configMapGenerator`, in the exact shape verified in Task 12 (service `http://kind-registry:5000`, `type: oci`). The `opa` sidecar image is `localhost:5001/opa:<pin>`. `api-dev` Application at wave `0`, automated with self-heal.
- [ ] **Step 3: Verify**

Run: `argocd app wait api-dev --health --timeout 600 && curl -s http://api.dev.localtest.me/buildinfo`
Expected: Healthy; the build info shows the git hash from Task 11.

- [ ] **Step 4: Commit** (`feat(deploy): Api and Worker in app-dev`)

### Task 14: E2E suite against the cluster

**Files:**
- Modify: `tests/Tests/E2E/AppHostFixture.cs:25-38`, `tests/Tests/E2E/ScenarioMatrixTests.cs`, `tests/Tests/E2E/CedarPolicyLoadTests.cs`, `tests/Tests/E2E/BrokerDeadLetterE2ETests.cs`, `README.md`, `requests.http` / `requests.rider.http` (add a `k8s-dev` environment, if the files use env blocks; otherwise a commented variables block)

**Interfaces:**
- Consumes: `E2E_TARGET=k8s`, `E2E_API_URL` (e.g. `http://api.dev.localtest.me`), `E2E_KEYCLOAK_URL` (e.g. `http://keycloak.localtest.me`).
- Trait: `[Trait("Target", "aspire-only")]` at class level on the three tests that use `OpaUri`, `CedarUri` or `GetConnectionStringAsync`.

- [ ] **Step 1:** In k8s mode `InitializeAsync` sets `KeycloakUri` and `ApiClient` from the env vars and starts nothing; `OpaUri`, `CedarUri` and `GetConnectionStringAsync` throw `InvalidOperationException("aspire-only")`; `DisposeAsync` disposes only the client.
- [ ] **Step 2:** Run the Aspire suite. Expected: PASS, unchanged.
- [ ] **Step 3:** Run against the cluster.

Run: `E2E_TARGET=k8s E2E_API_URL=http://api.dev.localtest.me E2E_KEYCLOAK_URL=http://keycloak.localtest.me dotnet test --project tests/Tests --filter-namespace "Tests.E2E" --filter-not-trait "Target=aspire-only"` twice in a row.
Expected: PASS both times. If the second run fails because of data left from the first, make the failing test use unique ids per run; do not wipe the database.

- [ ] **Step 4:** Open `http://dashboard.localtest.me`, find one trace spanning `api` and `worker`. Expected: present.
- [ ] **Step 5:** README: a short "Run on Kubernetes" section linking `deploy/README.md` and the E2E k8s command.
- [ ] **Step 6: Commit** (`test: run the E2E suite against the cluster`)

## Phase 6: Gatekeeper

### Task 15: Gatekeeper with tiered constraints

**Files:**
- Create: `deploy/argocd/apps/{gatekeeper,gatekeeper-templates,gatekeeper-constraints}.yaml`, `deploy/platform/gatekeeper-policies/templates/{k8srequiredresources,k8sdisallowedtags,k8sallowedrepos,k8srequiredlabels,k8sminreplicas}.yaml`, `deploy/platform/gatekeeper-policies/constraints/*.yaml`, `deploy/platform/gatekeeper-policies/tests/suite.yaml` and fixtures, `deploy/build/validate.sh`

**Interfaces:**
- Constraint matches use `namespaceSelector` on `lab.dev/tier` per the spec's Gatekeeper table; `replicas >= 2` matches `namespaces: [app-prod]` and kind `Deployment` with `app.kubernetes.io/name: api`.
- `validate.sh` runs `kustomize build --load-restrictor LoadRestrictionsNone` on every overlay and on `deploy/infra`, then `gator verify deploy/platform/gatekeeper-policies/tests`.

- [ ] **Step 1:** Copy the four library templates (`k8srequiredresources`, `k8sdisallowedtags`, `k8sallowedrepos`, `k8srequiredlabels`) from `gatekeeper-library` at the tag matching the pinned chart; confirm each checks `initContainers` (enable the library's `exemptImages`/init-container handling where it is a parameter). Write `k8sminreplicas` yourself (it is not in the library).
- [ ] **Step 2: Write the failing gator suite** first: for each constraint one passing fixture (the real api Deployment rendered from the dev overlay) and one failing fixture (`:latest` sidecar image, init container without limits, `docker.io` image in `app-dev`, missing `owner` label, prod Deployment with 1 replica).
- [ ] **Step 3:** Run `gator verify`. Expected: FAIL (no constraints yet).
- [ ] **Step 4:** Add constraints and Applications: `gatekeeper` chart (wave `-3`, `ServerSideApply=true`), templates (wave `-2`), constraints (wave `-1`, `SkipDryRunOnMissingResource=true`, `enforcementAction: deny`).
- [ ] **Step 5:** `validate.sh` passes. Then commit a sidecar image `openpolicyagent/opa:latest` to the dev overlay, push, and check the Argo UI. Expected: `api-dev` sync fails with the `k8sdisallowedtags` message; the live pods are unchanged. Revert and push.
- [ ] **Step 6: Commit** (`feat(deploy): Gatekeeper with tiered constraints`)

## Phase 7: Promotion

### Task 16: Staging and prod, and the promotion exercise

**Files:**
- Create: `deploy/apps/api/overlays/{staging,prod}/...` (same files as dev), `deploy/argocd/apps/{api-staging,api-prod}.yaml`, `deploy/build/promote.sh`
- Modify: `deploy/README.md`

**Interfaces:**
- `promote.sh <from-env> <to-env>` copies the `images:` tags and the OPA bundle tag from one overlay's `kustomization.yaml` / `opa-config.yaml` to the other's, and nothing else, then shows `git diff`. It does not commit.

- [ ] **Step 1:** Overlays with values from Global Constraints (staging: engine `cedar`, allowed `staging, prod`; prod: engine `opa`, allowed `prod`, api replicas 2). `api-staging` automated with self-heal; `api-prod` with no `automated` block.
- [ ] **Step 2:** Exercise, recorded in `deploy/README.md` as "Promote a policy change":
  1. Change one Cedar rule (for example a deny reason text), commit, run `build-policies.sh`, put the new tag in the dev overlay, commit and push. Expected: dev rolls automatically.
  2. `promote.sh dev staging`, commit, push. Expected: staging rolls (it runs Cedar, so the change is visible in its 403 reasons).
  3. `promote.sh staging prod`, commit, push. Expected: `api-prod` shows OutOfSync with the tag diff and does not roll; Sync in the UI; then it rolls.
- [ ] **Step 3:** `validate.sh` passes.
- [ ] **Step 4: Commit** (`feat(deploy): staging and prod overlays with promotion`)

### Task 17: Switch Argo to main after merge

Done only after the `k8s` PR merges.

- [ ] **Step 1:** Replace `targetRevision: k8s` with `main` in every file under `deploy/argocd/`, commit on `main` (or a short branch), push. Expected: `argocd app list` shows every app on `main`, Synced.

## Phase 8: Stretch

Not planned here. Each stretch item (ApplicationSets, Argo projects and RBAC, Argo Rollouts, NetworkPolicies, GHCR with GitHub Actions, Argo CD Image Updater) gets its own short design before work starts.
