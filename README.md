# OPA / Rego authorization demo

Shows how [Open Policy Agent](https://www.openpolicyagent.org/) and Rego allow or deny API requests by **role**, **tenant** and **environment**. .NET 10, Aspire, minimal APIs, Keycloak for OpenID Connect.

## Run

Requires the .NET 10 SDK and Docker.

```powershell
dotnet run --project src/AppHost
```

The Aspire dashboard lists every endpoint. Use the Keycloak `oidc` and Api `http` URLs in `requests.http`.

## What starts

| Resource | Image | Role |
|---|---|---|
| `keycloak` | Keycloak | OIDC provider, realm `opa-demo` imported from `src/AppHost/keycloak` |
| `policy-builder` | `infra/policy-builder` (wraps `openpolicyagent/opa`) | Watches `policies/`, runs `opa test`, publishes `bundle.tar.gz` |
| `bundle-server` | nginx | Serves the bundle |
| `opa` | `openpolicyagent/opa` | Polls the bundle every 1-3 seconds, answers `POST /v1/data/authz/decision` |
| `api` | `src/Api` | Validates the JWT, loads the document, asks OPA, returns 403 with reasons on deny |

The Api holds no allow/deny rules. Only `policies/authz.rego` does.

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

Send `X-Environment: dev|staging|prod` on every `/documents` call (400 otherwise).

| Rule | Reason returned on deny |
|---|---|
| reader: list, read. editor: + create, update. admin: + delete | `role does not permit action` |
| Tenant must match the document's tenant, except `platform-admin` | `cross-tenant access denied` |
| In `prod`, create/update need admin | `writes in prod require admin` |
| In `prod`, delete needs platform-admin | `deletes in prod are restricted to platform-admin` |
| Environment must be dev, staging or prod | `unknown environment` |

If OPA is unreachable the Api returns 503. It never allows by default.

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

## Notes

- NuGet packages and Docker images track latest. `Aspire.Hosting.Keycloak` only ships as a preview. If a version breaks the build, pin it in `Directory.Packages.props` and record why here.
- `NuGetAuditSuppress` hides a MessagePack advisory pulled in transitively by Aspire (AppHost and Tests only).
- Aspire randomizes host ports in some run modes; read the actual ports from the dashboard.
