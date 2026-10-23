# OPA / Rego authorization demo

Shows how [Open Policy Agent](https://www.openpolicyagent.org/) and Rego allow or deny API requests by **role**, **tenant** and **environment**. .NET 10, Aspire, minimal APIs, Keycloak for OpenID Connect.

## Run

Requires the .NET 10 SDK and Docker.

```powershell
dotnet run --project src/AppHost
```

The Aspire dashboard lists every endpoint. Use the Keycloak `oidc` and Api `http` URLs in `requests.http`.

## Run on Kubernetes

The same app on a local kind cluster, deployed by Argo CD from Git, with Gatekeeper guardrails and dev/staging/prod
environments. Setup, bootstrap and the promotion flow are in [`deploy/README.md`](deploy/README.md). Never run it
alongside Aspire.

## What starts

| Resource | Image | Role |
|---|---|---|
| `keycloak` | Keycloak | OIDC provider, realm `opa-demo` imported from `src/AppHost/keycloak` |
| `policy-builder` | `infra/policy-builder` (wraps `openpolicyagent/opa`) | Watches `policies/`, runs `opa test`, publishes `bundle.tar.gz` |
| `bundle-server` | nginx | Serves the bundle |
| `opa` | `openpolicyagent/opa` | Polls the bundle every 1-3 seconds, answers `POST /v1/data/authz/decision` |
| `cedar` | `permitio/cedar-agent` | Cedar sidecar agent, answers `POST /v1/is_authorized` from the policies file generated from `policies/authz.cedar` |
| `cosmos` | Cosmos DB Linux emulator | Source of truth for documents (`documents`), the audit trail (`audit`) and change feed leases (`leases`), all partitioned by `/tenant` |
| `rabbitmq` | RabbitMQ (management plugin) | Carries events from the outbox relay to the audit and cache-invalidation consumers |
| `valkey` | Valkey | Read cache for documents (never for authorization decisions) |
| `worker` | `src/Worker` | Relays the outbox to RabbitMQ, writes the audit trail, invalidates the cache |
| `api` | `src/Api` | Validates the JWT, loads the document, asks OPA or Cedar, returns 403 with reasons on deny |

Both `opa` and `cedar` sidecars start in Aspire. Which one the `api` calls is chosen by the AppHost launch profile: `https`/`http` use OPA (default), `https-cedar`/`http-cedar` use Cedar (e.g. `dotnet run --project src/AppHost --launch-profile https-cedar`). The profiles set `POLICY_ENGINE`, which the AppHost forwards to `api`.

The Api holds no allow/deny rules. Only `policies/authz.rego` (for OPA) and `policies/authz.cedar` (for Cedar) do.

## Events, audit and cache

Every write and every policy decision becomes an event that is recorded **transactionally** and delivered **at least once**:

1. **Outbox.** A document write is one Cosmos transactional batch holding the document and its `document.changed` outbox item (same `/tenant` partition, same container), so neither exists without the other. Policy decisions (`policy.decision`) are written as standalone outbox items. If the decision cannot be recorded the Api answers 503, so no decision is acted on without an audit trail. Outbox items expire after 7 days.
2. **Relay.** The Worker reads the `documents` change feed, keeps only `type = outbox` items and publishes them to the `policy-demo.events` topic exchange with publisher confirms. A failed publish is not checkpointed, so the batch is retried.
3. **Consumers.** Two queues, each with its own consumer and dead-letter queue (`<queue>.dlq`, after 5 attempts):
   * `audit` upserts an entry into the `audit` container keyed by the event id, so redelivery is harmless.
   * `cache-invalidation` evicts the document and tenant list from Valkey on `document.changed`.
4. **Read cache.** Documents are read through Valkey (60 second TTL). Policy decisions are never cached. For `GET /documents` (the list) authorization runs before the cache is read. For get, update and delete by id the document is loaded first, because its tenant is part of the policy input, so the cache is read before the decision; its data is only returned to the caller once policy allows. Writes evict immediately. If Valkey is down the Api reads Cosmos directly; the cache never fails a request.
5. **Audit trail.** `GET /audit?tenant=&from=&to=&pageSize=&continuation=` is itself authorized: the `audit` action needs `admin` in the tenant, or `platform-admin` for any tenant.

Run the Api alone without Cosmos (`ConnectionStrings:cosmos` unset) and it falls back to the in-memory store, so unit tests need no Docker.

Logs are structured JSON on the console (Serilog compact format). Under Aspire they also flow to the dashboard over OTLP. Authorization headers, tokens and document contents are never logged; events carry a `TraceId` so one request can be followed from the Api through the relay to the audit write.

## Users

Password for all: `Passw0rd!`

| User | Role | Tenant |
|---|---|---|
| `reader-acme` | reader | acme |
| `editor-acme` | editor | acme |
| `admin-acme` | admin | acme |
| `editor-globex` | editor | globex |
| `admin-globex` | admin | globex |
| `platform-admin` | platform-admin | platform |

## Rules

Send `X-Environment: dev|staging|prod` on every `/documents` and `/audit` call (400 otherwise).

| Rule | Reason returned on deny |
|---|---|
| reader: list, read. editor: + create, update. admin: + delete, audit | `role does not permit action` |
| Tenant must match the document's tenant, except `platform-admin` | `cross-tenant access denied` |
| In `prod`, create/update need admin | `writes in prod require admin` |
| In `prod`, delete needs platform-admin | `deletes in prod are restricted to platform-admin` |
| Environment must be dev, staging or prod | `unknown environment` |

If OPA is unreachable the Api returns 503. It never allows by default.

## Policy files (Cedar)

The Cedar engine's rules live in one hand-edited file, `policies/authz.cedar`. Each rule is a
multi-line Cedar statement that starts with an `@id("...")` annotation. The id is required, must
be unique, and is what `permitio/cedar-agent` reports, so the ids of the `forbid` rules are the
user-visible deny reasons.

On every start the AppHost generates `infra/cedar/policies.json` from that file (a JSON array of
`{"id": "...", "content": "..."}`, the format the agent loads through `CEDAR_AGENT_POLICIES`) and
bind-mounts it into the `cedar` container. The JSON is git-ignored; never edit it. If
`authz.cedar` cannot be split, the AppHost fails at startup with line-numbered errors.

To change a Cedar rule, edit `policies/authz.cedar` and restart the AppHost. The agent only reads
the file at startup.

## Change a policy

1. Edit `policies/authz.rego` and its tests in `policies/authz_test.rego`.
2. Within a few seconds the policy-builder tests and rebuilds the bundle, and OPA reloads it. No restart.
3. If `opa test` fails, the previous bundle stays live and the policy-builder log shows why.

Run the policy tests alone:

```powershell
docker run --rm --volume "${PWD}/policies:/policies" openpolicyagent/opa:latest test /policies --verbose
```

## Tests

```powershell
dotnet test --project tests/Tests
```

`Tests.E2E` starts the whole stack through Aspire (needs Docker). `ScenarioMatrixTests` queries OPA for every user x tenant x environment x action and prints an allow/deny table with reasons in the test output.

Against the Kubernetes dev environment instead (no Aspire; tests that need the sidecars or the broker directly are skipped):

```bash
E2E_TARGET=k8s E2E_API_URL=http://api.dev.localtest.me E2E_KEYCLOAK_URL=http://keycloak.localtest.me \
  dotnet test --project tests/Tests --filter-namespace "Tests.E2E" --filter-not-trait "Target=aspire-only"
```

## Notes

- NuGet packages and Docker images track latest. `Aspire.Hosting.Keycloak` only ships as a preview. If a version breaks the build, pin it in `Directory.Packages.props` and record why here.
- `NuGetAuditSuppress` hides a MessagePack advisory pulled in transitively by Aspire (AppHost and Tests only).
- Aspire randomizes host ports in some run modes; read the actual ports from the dashboard.
