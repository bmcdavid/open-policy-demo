# OPA / Rego Authorization Demo — Design

## Intent
A discovery-and-learning demo showing how Open Policy Agent (OPA) and Rego policies allow or deny API requests based on **role**, **tenant**, and **environment**. The reader should be able to run it, try requests as different users, edit a `.rego` file, and watch decisions change without restarting anything.

## Goals
- .NET 10 + .NET Aspire AppHost orchestrating everything, including containers.
- API follows the conventions of the sibling `C:\_dev\repos\portal` repo (see Conventions).
- Authentication via a Docker-based OpenID Connect provider (Keycloak), pre-seeded with demo users.
- OPA runs as a container; the API asks OPA for each decision over HTTP.
- Policies are managed the conventional way: `.rego` in Git → `opa build` → versioned bundle → bundle server → OPA polls and hot-reloads.
- Policies are unit-tested with `opa test`; the API has integration tests against real decisions.
- A way to try many user/tenant/environment combinations and see allow/deny results.

## Non-goals
- Production hardening (TLS, secrets management, HA, bundle signing).
- A policy-editing UI or API that writes policies to a database.
- Persistent business data; the demo resource store is in-memory.
- A Web/UI project.

## Anti-goals
- Authorization logic duplicated in C#. The API must not hold allow/deny rules; Rego is the only place they live.
- Needing a restart or manual copy step for a policy change to take effect.
- A demo too large to read in one sitting.

## Architecture
Aspire AppHost starts:
1. **Keycloak** (container) with an imported realm: demo users, realm roles, and `tenant` / `environment` claims in tokens.
2. **policy-builder** (one-shot container, official `openpolicyagent/opa` image) runs `opa test` then `opa build` over `policies/`, writing `bundle.tar.gz` to a shared volume.
3. **bundle-server** (nginx container) serves the bundle from that volume.
4. **OPA** (container) configured with the bundle service; polls every few seconds.
5. **Api** (project) validates the Keycloak JWT, builds an OPA input, queries OPA, enforces the decision.

Flow per request: JWT validated → endpoint filter builds input `{ subject: {id, roles, tenant}, environment, action, resource: {type, tenant, id} }` → POST to OPA `/v1/data/<package>/decision` → result `{ allow, reasons[] }` → 403 with reasons (demo only) or proceed.

To change a policy: edit `.rego`, re-run the policy-builder (AppHost resource command or `docker compose`-style rerun), OPA picks up the new bundle within the poll interval.

## Policy behavior (illustrative; builder may adjust details)
- Roles: `reader`, `editor`, `admin`.
- Tenants: `acme`, `globex`. A subject may only touch resources in their own tenant, except a `platform-admin` role that spans tenants.
- Environments: `dev`, `staging`, `prod`, supplied as a request context value (the API is configured with, or the request selects, a simulated environment — builder decides the mechanism, e.g. `X-Environment` header validated against an allow-list).
- Rules: readers read; editors read/write; admins also delete; writes in `prod` require admin; deletes in `prod` are denied for everyone except platform-admin; cross-tenant access denied.
- Every deny returns a machine-readable reason so the demo teaches *why*.

## Conventions (copied from `portal`)
- `Directory.Build.props`: net10.0, Nullable enable, ImplicitUsings disable, LangVersion latest, AnalysisMode All, TreatWarningsAsErrors true, company/build-info properties as in portal (build-info attribute optional).
- `Directory.Packages.props` with central package management.
- `global.json` selecting the Microsoft.Testing.Platform test runner; xunit.v3, AwesomeAssertions as in portal.
- `.editorconfig` (4-space C#, utf-8-bom, 2-space xml/json), `.gitignore`, `.slnx` solution.
- Projects: `src/AppHost`, `src/Api`, `src/Core`, `tests/Tests`.
- Api: `Program.cs`, `CompositionRoot.cs` (`Compose`), `RequestPipeline.cs`, `Features/<Feature>/ConfigureServicesAndEndpoints.cs` with `ConfigureX` / `MapXEndpoints`, `MapGroup`-based minimal APIs, handler classes behind `IEndpointHandler<TIn,TOut>`, `GlobalUsings.cs`.
- Folder layout otherwise: `policies/` (rego + `_test.rego`), `docs/`, `README.md`.

## Demo API surface (my call)
A small in-memory "documents" resource (list, get, create, update, delete) scoped by tenant, plus `/whoami` (echoes claims) and `/buildinfo` as in portal. Small enough that policy, not CRUD, is the focus.

## Trying it out (dealer's choice — my call)
- `requests.http` file with token-fetch examples for each demo user.
- A scenario-matrix runner (a test or console script) that iterates user × tenant × environment × action and prints an allow/deny table with reasons. Implemented as an integration test project output that can also be run standalone.
- Scalar/OpenAPI UI is optional.

## Decisions made by me (not confirmed with the user)
- Keycloak as the OIDC provider (Authority discovery over Aspire-provided URL).
- nginx as the bundle server; OPA configured to poll.
- Decision queried over HTTP (not embedded WASM / not the Rego .NET SDK), because that mirrors the common sidecar deployment.
- In-memory data.
- Environment passed per request rather than running three API instances.

## Builder-check resolutions (my calls)
- **Portal reference:** `C:\_dev\repos\portal` exists and is readable. The first plan task is to read and replicate its `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `Api/CompositionRoot.cs`, `Api/RequestPipeline.cs`, `Api/Features/IEndpointHandler.cs`, and a feature's `ConfigureServicesAndEndpoints.cs`. Its `src/AppHost` is empty, so the Aspire host is new. Portal's `InternalsVisibleTo` entries reference `Api`, `Web`, `Tests`.
- **Policy rebuild:** a long-running `policy-builder` container watches a bind-mounted `policies/` folder, and on any change runs `opa test` then `opa build` and writes the bundle. No manual step. If tests fail, the old bundle stays in place.
- **Environment:** request-level only, via `X-Environment` header validated against `dev|staging|prod` (missing → 400). It is not in the token.
- **Roles and users:** `platform-admin` is a realm role. Users may hold multiple roles. Each user has one `tenant` claim. Demo users: `reader-acme`, `editor-acme`, `admin-acme`, `editor-globex`, `admin-globex`, `platform-admin` (tenant `platform`). Shared demo password.
- **Resource tenant:** `get`/`update`/`delete` handlers load the document first, then pass its tenant as `resource.tenant` to OPA (cross-tenant on an existing doc returns 403; a missing doc returns 404). `create` and `list` take the tenant from an optional `tenant` query/body value, defaulting to the subject's tenant, and OPA decides. The handler never contains allow/deny rules.
- **Scenario matrix:** runs against OPA directly with synthetic input (fast, no Keycloak), as a test and standalone console output. A smaller set of end-to-end tests uses Aspire's `DistributedApplicationTestingBuilder` with real tokens; these need Docker.
- **Core contents:** OPA client, input/decision models, document model and in-memory store, shared constants.
- **Versions:** latest stable Aspire (13.x at build time), pin Keycloak, nginx and OPA image tags.

## Left to the builder
Exact package versions, rego file organization, realm JSON contents, Aspire resource wiring details (volumes, wait-for ordering), how the policy rebuild is triggered, test structure, README content.
