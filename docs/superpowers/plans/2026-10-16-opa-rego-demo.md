# OPA / Rego Authorization Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A .NET 10 / Aspire demo where a minimal-API service authorizes requests by role, tenant and environment through Rego policies served to OPA as a hot-reloading bundle, with Keycloak as the OIDC provider.

**Architecture:** AppHost starts Keycloak, a policy-builder watcher (test + `opa build` into a shared volume), an nginx bundle server, and OPA (polls the bundle). The Api validates Keycloak JWTs, loads the resource, asks OPA for `authz/decision`, and returns 403 with reasons on deny. No allow/deny rule exists in C#.

**Tech Stack:** .NET 10, Aspire (latest stable), ASP.NET Core minimal APIs, JwtBearer, xunit.v3 + AwesomeAssertions on Microsoft.Testing.Platform, OPA, Keycloak, nginx, Docker.

**Spec:** `docs/superpowers/specs/2026-10-16-opa-rego-demo-design.md`

## Global Constraints

- Latest stable NuGet packages and `:latest` Docker image tags (user amendment 2026-10-16). Pin only if a concrete issue appears; record the issue in `README.md`.
- Conventions copied from `C:\_dev\repos\portal`: `Directory.Build.props` (net10.0, Nullable enable, ImplicitUsings disable, LangVersion latest, AnalysisMode All, TreatWarningsAsErrors true), `Directory.Packages.props` (central package management), `global.json` (`"test": {"runner": "Microsoft.Testing.Platform"}`), `.editorconfig`, `.slnx`.
- Layout: `src/AppHost`, `src/Api`, `src/Core`, `tests/Tests`, `policies/`, `docs/`, `README.md`.
- Api layout: `Program.cs`, `CompositionRoot.cs` (`Compose`), `RequestPipeline.cs`, `Features/<Feature>/ConfigureServicesAndEndpoints.cs` with `ConfigureX` / `MapXEndpoints`, `MapGroup` minimal APIs, `IEndpointHandler<TIn,TOut>` handlers, `GlobalUsings.cs`.
- Rego is the only home of allow/deny rules. C# never branches on role, tenant or environment to decide access.
- Roles: `reader`, `editor`, `admin`, `platform-admin`. Tenants: `acme`, `globex`, `platform`. Environments: `dev`, `staging`, `prod` via `X-Environment` header only.
- Git commits: `<type>(<scope>): <subject>`, imperative, ≤55 chars, one line unless complex, **no AI co-author trailer** (user's global rule overrides the default attribution reminder).
- Use long-form CLI flags in all scripts and commands.
- Docker is required to run the app and the Aspire-backed tests.

## Review Focus

Each line is pinned by a test in the task named in brackets.

1. Missing `X-Environment` header → 400, invalid value (`production`, `PROD ` ) → 400 [Task 5].
2. OPA unreachable or returns an error → request denied with 503, never allowed [Task 3, Task 5].
3. Token with no `tenant` claim or no roles → denied, not a 500 [Task 4, Task 5].
4. User with multiple roles gets the union of permissions; `platform-admin` crosses tenants [Task 2].
5. Document that does not exist → 404 (not 403); document in another tenant → 403 with a reason [Task 5].
6. Policy edit that fails `opa test` leaves the previous bundle live [Task 6].

---

### Task 1: Repository scaffold

**Files:**
- Create: `.gitignore`, `.editorconfig`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `opa.slnx`, `src/Core/Core.csproj`, `src/Api/Api.csproj`, `src/AppHost/AppHost.csproj`, `tests/Tests/Tests.csproj`, `src/Api/GlobalUsings.cs`, `src/Core/GlobalUsings.cs`

**Interfaces:**
- Produces: a solution that builds clean with warnings-as-errors; project names `Core`, `Api`, `AppHost`, `Tests`; `Core` exposes internals to `Api` and `Tests` (`InternalsVisibleTo` in `Directory.Build.props`).

- [ ] **Step 1:** `git init` in `C:\_dev\repos\opa`. Read portal's `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `.gitignore`, `src/Core/BuildInfoAttribute.cs`, and wherever `UseBuildInfoRoute` is defined (`Core/Features/...`); copy them, removing portal-only packages and the `Web` entry from `InternalsVisibleTo`.
- [ ] **Step 2:** Generate the AppHost with `dotnet new aspire-apphost` (check `dotnet new list aspire` for the current template) to learn the current `Aspire.AppHost.Sdk` version and csproj shape; move it to `src/AppHost`, set `IsAspireHost` true. Create `Core` (class library), `Api` (`Microsoft.NET.Sdk.Web`), `Tests` (xunit.v3 exe, `Microsoft.Testing.Platform`, references Core, Api, AppHost) with central versions resolved to latest stable (`dotnet package search` / `dotnet add package` then move versions into `Directory.Packages.props`).
- [ ] **Step 3:** Write `opa.slnx` listing the four projects plus solution items.
- [ ] **Step 4:** Run `dotnet build opa.slnx`. Expected: `Build succeeded` with 0 warnings. If analyzers fail an unavoidable generated file, add a scoped `NoWarn` in that project only and note it in the commit body.
- [ ] **Step 5:** Commit `chore(repo): scaffold solution and conventions`.

---

### Task 2: Rego policy and policy tests

**Files:**
- Create: `policies/authz.rego`, `policies/authz_test.rego`

**Interfaces:**
- Produces: OPA document `data.authz.decision` returning `{ "allow": bool, "reasons": [string] }` for input
  `{ "subject": {"id","roles":[...],"tenant"}, "environment": "dev|staging|prod", "action": "list|read|create|update|delete", "resource": {"type","tenant","id"} }`.
  Default decision: `allow=false`, reasons `["no rule allowed the request"]`. Denied requests carry at least one specific reason string (listed below, exact text).

Rules (spec "Policy behavior"):
- Role grants: `reader` → list, read; `editor` → + create, update; `admin` → + delete; `platform-admin` → everything and any tenant.
- Tenant: `subject.tenant == resource.tenant` unless `platform-admin`. Deny reason `"cross-tenant access denied"`.
- `prod`: create/update require `admin` or `platform-admin` (reason `"writes in prod require admin"`); delete requires `platform-admin` (reason `"deletes in prod are restricted to platform-admin"`).
- No role grants the action → reason `"role does not permit action"`.
- Roles are a set; multiple roles are unioned.

- [ ] **Step 1: Write failing tests** in `authz_test.rego` (package `authz_test`), one named test per row. Each asserts on `data.authz.decision.allow` (and, for denials, that the exact reason string is in `reasons`):
  - `test_reader_can_read_own_tenant_dev` (allow), `test_reader_cannot_update` (deny, `"role does not permit action"`)
  - `test_editor_can_update_dev`, `test_editor_cannot_delete_dev`, `test_admin_can_delete_dev`, `test_admin_cannot_delete_prod`
  - `test_editor_cannot_update_prod`, `test_admin_can_update_prod`, `test_platform_admin_can_delete_prod`
  - `test_cross_tenant_read_denied` (acme reader on globex doc), `test_platform_admin_crosses_tenants`
  - `test_multiple_roles_union` (`["reader","editor"]` can update)
  - `test_missing_roles_denied`, `test_empty_input_denied` (default decision, no error)
- [ ] **Step 2:** Run (PowerShell): `docker run --rm --volume "${PWD}/policies:/policies" openpolicyagent/opa:latest test /policies --verbose`. Expected: FAIL (undefined `data.authz.decision`).
- [ ] **Step 3:** Implement `authz.rego` (`package authz`, `import rego.v1`): a `deny` partial set of reason strings, an `role_permits` rule, and `decision := {"allow": allow, "reasons": reasons}` where `allow` is `role_permits` with `count(deny) == 0`, and `reasons` is `deny`, or the default reason when empty and not allowed.
- [ ] **Step 4:** Re-run the command from Step 2. Expected: all PASS.
- [ ] **Step 5:** Commit `feat(policy): add role, tenant, environment rego`.

---

### Task 3: Core — policy client, models, document store

**Files:**
- Create: `src/Core/Features/Authorization/{PolicyModels.cs,IPolicyClient.cs,OpaPolicyClient.cs,PolicyEngineUnavailableException.cs,PolicyActions.cs,DemoEnvironments.cs}`, `src/Core/Features/Documents/{Document.cs,IDocumentStore.cs,InMemoryDocumentStore.cs}`
- Test: `tests/Tests/Core/OpaPolicyClientTests.cs`, `tests/Tests/Core/InMemoryDocumentStoreTests.cs`

**Interfaces:**
- Produces:
  - `record PolicySubject(string Id, IReadOnlyList<string> Roles, string Tenant)`
  - `record PolicyResource(string Type, string Tenant, string? Id)`
  - `record PolicyInput(PolicySubject Subject, string Environment, string Action, PolicyResource Resource)` — serialized camelCase to match the rego input
  - `record PolicyDecision(bool Allow, IReadOnlyList<string> Reasons)`
  - `interface IPolicyClient { Task<PolicyDecision> DecideAsync(PolicyInput input, CancellationToken cancellationToken); }`
  - `class OpaPolicyClient(HttpClient http) : IPolicyClient` — POSTs `{ "input": ... }` to `v1/data/authz/decision`, reads `result`; throws `PolicyEngineUnavailableException` on `HttpRequestException`, non-2xx, timeout, or a response with no `result`.
  - `static class PolicyActions { const string List="list", Read="read", Create="create", Update="update", Delete="delete"; }`
  - `static class DemoEnvironments { static bool TryParse(string? value, out string environment) }` — exact lowercase match of `dev|staging|prod` only; no trimming.
  - `record Document(Guid Id, string Tenant, string Title)`
  - `interface IDocumentStore { IReadOnlyList<Document> List(string tenant); Document? Get(Guid id); Document Add(string tenant, string title); Document? Update(Guid id, string title); bool Remove(Guid id); }`; `InMemoryDocumentStore` is thread-safe and seeds one document per tenant `acme` and `globex`.

- [ ] **Step 1: Write failing tests** with a hand-written `HttpMessageHandler` stub: `DecideAsync_returns_decision_from_result`, `DecideAsync_posts_input_wrapper_to_authz_decision_path`, `DecideAsync_throws_unavailable_on_500`, `DecideAsync_throws_unavailable_on_connection_failure`, `DecideAsync_throws_unavailable_when_result_missing` (OPA returns `{}` when the package is not loaded). Store tests: `List_returns_only_requested_tenant`, `Update_missing_returns_null`, `Remove_missing_returns_false`. Add `DemoEnvironments` cases for `dev`, `prod`, `PROD`, ` prod`, empty, null.
- [ ] **Step 2:** `dotnet test --project tests/Tests --filter-class "*Core*"`. Expected: FAIL (types undefined / build error).
- [ ] **Step 3:** Implement the interfaces above. Use `System.Text.Json` with `JsonSerializerDefaults.Web`.
- [ ] **Step 4:** Re-run Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): add opa client and document store`.

---

### Task 4: Api skeleton — auth, pipeline, whoami

**Files:**
- Create: `src/Api/{Program.cs,CompositionRoot.cs,RequestPipeline.cs,GlobalUsings.cs}`, `src/Api/Features/IEndpointHandler.cs`, `src/Api/Features/Home/ConfigureServicesAndEndpoints.cs`, `src/Api/Features/Authorization/{ConfigureServicesAndEndpoints.cs,PolicyEnforcer.cs,CurrentSubject.cs}`
- Test: `tests/Tests/Api/WhoAmITests.cs`, `tests/Tests/Api/PolicyEnforcerTests.cs`

**Interfaces:**
- Consumes: `IPolicyClient`, `PolicyInput`, `PolicyDecision`, `PolicyEngineUnavailableException`.
- Produces:
  - `internal interface IEndpointHandler<TIn,TOut>` (copy portal's signature).
  - `internal static class CurrentSubject { static PolicySubject From(ClaimsPrincipal user) }` — id from `sub`, roles from repeated/array `roles` claim, tenant from `tenant` claim; missing values yield empty roles / empty tenant (never throws).
  - `internal interface IPolicyEnforcer { Task<IResult?> EnforceAsync(HttpContext http, string action, PolicyResource resource, CancellationToken ct); }` — returns `null` when allowed; `Results.Json(new { reasons }, statusCode: 403)` when denied; `Results.Problem(statusCode: 503, title: "policy engine unavailable")` when the client throws. Reads environment from `http.Items["environment"]` (set in Task 5).
  - Routes `GET /whoami` (authorized; returns id, roles, tenant), `GET /buildinfo` (copied pattern). JWT bearer with `MapInboundClaims = false`, `Authority` / `Audience` from config keys `Auth:Authority`, `Auth:Audience`; `RequireHttpsMetadata` false when `Environment.IsDevelopment()`. `Opa:BaseAddress` config binds `OpaPolicyClient`'s `HttpClient` (2-second timeout). `AddServiceDefaults` is not used; keep OpenTelemetry out unless Aspire template requires it.

- [ ] **Step 1: Write failing tests:** `PolicyEnforcer`: `EnforceAsync_returns_null_when_allowed`, `EnforceAsync_returns_403_with_reasons_when_denied`, `EnforceAsync_returns_503_when_engine_unavailable` (fake `IPolicyClient`). `CurrentSubject.From`: `Roles_read_from_roles_claim`, `Missing_tenant_and_roles_yield_empty_values`. `WhoAmITests` via `WebApplicationFactory<Program>` (add `public partial class Program` visibility as needed): `whoami_without_token_returns_401`.
- [ ] **Step 2:** `dotnet test --project tests/Tests --filter-class "*Api*"`. Expected: FAIL.
- [ ] **Step 3:** Implement as specified, following portal's `CompositionRoot.Compose` / `RequestPipeline.ConfigureRequestPipelineForApi` shape (security headers, UseAuthentication before UseAuthorization).
- [ ] **Step 4:** Re-run Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(api): add auth pipeline and policy enforcer`.

---

### Task 5: Api — documents feature and environment filter

**Files:**
- Create: `src/Api/Features/Documents/{ConfigureServicesAndEndpoints.cs,ListDocumentsHttpRequestHandler.cs,GetDocumentHttpRequestHandler.cs,CreateDocumentHttpRequestHandler.cs,UpdateDocumentHttpRequestHandler.cs,DeleteDocumentHttpRequestHandler.cs,EnvironmentHeaderFilter.cs}`, `src/Core/ApiRoutes.cs`
- Test: `tests/Tests/Api/DocumentsEndpointTests.cs`

**Interfaces:**
- Consumes: `IPolicyEnforcer`, `IDocumentStore`, `PolicyActions`, `DemoEnvironments`, `CurrentSubject`.
- Produces: route group `/documents` (requires authentication) with `EnvironmentHeaderFilter` (`IEndpointFilter`): reads `X-Environment`, rejects with 400 `{ "error": "X-Environment must be one of dev, staging, prod" }` when `DemoEnvironments.TryParse` fails, else sets `HttpContext.Items["environment"]`.
  - `GET /documents?tenant=` (action `list`, resource tenant = query value or subject tenant; returns only that tenant's documents when allowed)
  - `GET /documents/{id:guid}` (`read`), `POST /documents` body `{ title, tenant? }` (`create`, tenant defaults to subject's), `PUT /documents/{id:guid}` body `{ title }` (`update`), `DELETE /documents/{id:guid}` (`delete`).
  - get/update/delete: load document first; missing → 404 before any policy call; present → enforce with `PolicyResource("document", doc.Tenant, id)`.
  - Each handler implements `IEndpointHandler<TIn, IResult>`.

- [ ] **Step 1: Write failing tests** using `WebApplicationFactory<Program>` with a fake `IPolicyClient` replacing OPA and a test auth scheme issuing claims: `Missing_environment_header_returns_400`, `Invalid_environment_values_return_400` (`production`, `PROD`, ` prod`), `Denied_decision_returns_403_with_reasons`, `Allowed_get_returns_document`, `Missing_document_returns_404_without_calling_policy`, `Engine_unavailable_returns_503_never_200`, `Token_without_tenant_or_roles_is_passed_to_policy_as_empty_and_denied_when_policy_denies`, `Create_defaults_tenant_to_subject_tenant`, `List_passes_requested_tenant_to_policy`, `Policy_input_carries_environment_action_and_document_tenant` (assert on the captured `PolicyInput`).
- [ ] **Step 2:** `dotnet test --project tests/Tests --filter-class "*DocumentsEndpoint*"`. Expected: FAIL.
- [ ] **Step 3:** Implement per Interfaces; handlers register as transient in `ConfigureDocuments`; `InMemoryDocumentStore` as singleton.
- [ ] **Step 4:** Re-run Step 2. Expected: PASS.
- [ ] **Step 5:** Commit `feat(api): add tenant-scoped documents endpoints`.

---

### Task 6: Infrastructure — Keycloak realm, policy-builder, nginx, OPA config

**Files:**
- Create: `src/AppHost/keycloak/opa-demo-realm.json`, `infra/policy-builder/{Dockerfile,watch.sh}`, `infra/bundle-server/nginx.conf`, `infra/opa/opa-config.yaml`

**Interfaces:**
- Produces:
  - Realm `opa-demo` at `http://localhost:8080/realms/opa-demo`; public client `demo-api` (direct access grants enabled for demo password-grant); mappers: realm roles → `roles` (multivalued, access token), user attribute `tenant` → `tenant`, audience `demo-api`. Users (password `Passw0rd!`): `reader-acme`, `editor-acme`, `admin-acme` (tenant `acme`), `editor-globex`, `admin-globex` (tenant `globex`), `platform-admin` (tenant `platform`, role `platform-admin`). Realm roles: `reader`, `editor`, `admin`, `platform-admin`.
  - `policy-builder` image: `FROM openpolicyagent/opa:latest AS opa` then a minimal Alpine stage copying `/opa` (call: the official opa image has no shell, so a loop needs a wrapper). `watch.sh` loops every 2 seconds: hashes `/policies/*.rego`, on change runs `opa test /policies`; on success runs `opa build --bundle /policies --output /bundles/bundle.tar.gz.tmp` and renames atomically to `/bundles/bundle.tar.gz`; on test failure logs and leaves the previous bundle. Runs once at start.
  - nginx serves `/bundles` as static files at `/`.
  - `opa-config.yaml`: service `bundle-server` at `http://bundle-server:80`; bundle `authz` resource `bundle.tar.gz`, polling `min_delay_seconds: 1`, `max_delay_seconds: 3`; decision logs to console.

- [ ] **Step 1:** Write the files above.
- [ ] **Step 2:** Verify the watcher: build and run the image with `policies/` and a temp bundle volume (`docker build`, `docker run --rm --detach`), then confirm `/bundles/bundle.tar.gz` appears. Expected: file exists within ~5 seconds.
- [ ] **Step 3:** Break a rule in `authz_test.rego` temporarily (assert the wrong value), wait 5 seconds, confirm the bundle's modified timestamp is unchanged and logs show the test failure; revert the edit and confirm a new bundle is built.
- [ ] **Step 4:** Commit `feat(infra): add keycloak realm and bundle pipeline`.

---

### Task 7: AppHost wiring and end-to-end tests

**Files:**
- Modify: `src/AppHost/AppHost.cs`
- Create: `tests/Tests/E2E/AppHostFixture.cs`, `tests/Tests/E2E/DocumentsE2ETests.cs`, `tests/Tests/E2E/ScenarioMatrixTests.cs`

**Interfaces:**
- Consumes: infra files from Task 6, Api config keys `Auth:Authority`, `Auth:Audience`, `Opa:BaseAddress`.
- Produces:
  - AppHost resources: `keycloak` (`Aspire.Hosting.Keycloak`, fixed host port 8080, realm imported from `keycloak/`), `bundles` named volume, `policy-builder` (`AddDockerfile`, bind-mount `../../policies` → `/policies`, volume `bundles:/bundles`), `bundle-server` (nginx, volume `bundles:/usr/share/nginx/html:ro`, config bind mount), `opa` (`openpolicyagent/opa:latest`, args `run --server --config-file=/config/opa-config.yaml`, health check `/health?bundles`, waits for `bundle-server` and `policy-builder`), `api` (project reference, waits for `opa` and `keycloak`, env `Auth__Authority`, `Auth__Audience=demo-api`, `Opa__BaseAddress`).
  - `AppHostFixture` (xunit.v3 assembly/collection fixture): starts the AppHost with `DistributedApplicationTestingBuilder`, waits for `api` healthy, exposes `HttpClient ApiClient`, `Uri OpaUri`, and `Task<string> GetTokenAsync(string username)` (password grant to Keycloak).
- Scenario matrix: `ScenarioMatrixTests` iterates user × tenant × environment × action, posts synthetic input directly to OPA (no Keycloak), compares to an expected table kept in the test file, and writes an allow/deny table with reasons through `ITestOutputHelper` so it is also readable as console output.

- [ ] **Step 1: Write failing tests.** E2E (real tokens through the API): `reader_acme_can_get_acme_document_in_dev`, `reader_acme_cannot_update`, `editor_acme_gets_403_updating_prod`, `admin_acme_gets_403_deleting_prod`, `platform_admin_can_delete_prod`, `editor_globex_cannot_read_acme_document`, `no_token_returns_401`. Matrix: `Matrix_matches_expected_decisions` covers all six users × `acme`/`globex` × three environments × five actions.
- [ ] **Step 2:** Run `dotnet test --project tests/Tests --filter-class "*E2E*"` (needs Docker). Expected: FAIL (fixture / resources missing).
- [ ] **Step 3:** Implement `AppHost.cs` and the fixture. If a container name cannot be resolved between containers, use the Aspire-provided endpoint reference rather than hard-coding hostnames and update `opa-config.yaml` accordingly.
- [ ] **Step 4:** Re-run Step 2. Expected: PASS. Then run `dotnet run --project src/AppHost` and open the dashboard to confirm all resources are healthy.
- [ ] **Step 5:** Hot-reload check: edit `policies/authz.rego` (e.g. allow editors to delete in dev), wait ~5 seconds, confirm a delete as `editor-acme` now returns 200; revert and confirm 403 again.
- [ ] **Step 6:** Commit `feat(apphost): orchestrate keycloak, opa, bundle server`.

---

### Task 8: Try-it assets and README

**Files:**
- Create: `requests.http`, `README.md`

- [ ] **Step 1:** `requests.http`: token requests for each demo user (password grant against `http://localhost:8080/realms/opa-demo/protocol/openid-connect/token`, client `demo-api`), then sample `/whoami` and `/documents` calls with `X-Environment` varied. Variables use the Api URL from the Aspire dashboard (note this in a comment).
- [ ] **Step 2:** `README.md` (short): what it demonstrates; run instructions (`dotnet run --project src/AppHost`); user/role/tenant table; policy rule table; how to edit a policy and watch hot reload; how to run `opa test` and the scenario matrix; note that all images and packages float to latest and what to do if one breaks.
- [ ] **Step 3:** Run every `requests.http` request once against the running app; confirm responses match the README rule table.
- [ ] **Step 4:** Commit `docs: add README and request samples`.

---

## Self-Review

- Spec coverage: Aspire/.NET 10 (T1, T7), portal conventions (T1, T4), Keycloak (T6, T7), OPA container + bundle flow (T6, T7), policy rules (T2), tenant/env/role demo (T2, T5, T7), opa tests (T2), integration tests and matrix (T7), try-it assets (T8), no C# rules (Global Constraints, T4/T5 structure).
- Deviations from the spec, called out: (1) policy-builder uses a thin Alpine wrapper around the official `opa` binary because the official image has no shell; (2) the scenario matrix runs inside the Aspire fixture (OPA queried directly, synthetic input) rather than a standalone OPA container, trading speed for one less moving part; (3) handlers call `IPolicyEnforcer` rather than an endpoint filter for resource-level decisions, because the document must be loaded first.
- Names verified consistent: `PolicyInput`, `PolicyDecision`, `IPolicyClient.DecideAsync`, `IPolicyEnforcer.EnforceAsync`, `DemoEnvironments.TryParse`, `PolicyActions`.
