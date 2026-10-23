# Kubernetes lab

This repo's authorization demo on a local [kind](https://kind.sigs.k8s.io/) cluster, deployed by
[Argo CD](https://argo-cd.readthedocs.io/) from Git, with [Gatekeeper](https://open-policy-agent.github.io/gatekeeper/)
guardrails and three environments (dev, staging, prod) promoted by Git commits.
Design and decisions: [`docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md`](../docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md).

> **Never run Aspire (`dotnet run --project src/AppHost`) while the cluster is up**, and never `docker stop` the
> cluster's nodes. To switch, delete the cluster and rebuild it later (section 7); a rebuild takes about 5 minutes.

```
 laptop ── http://*.localtest.me (127.0.0.1:80) ──► Envoy Gateway ──► api pods in app-dev | app-staging | app-prod
                                                                       (api + OPA + Cedar sidecars) + worker
 kind cluster "lab" (3 Docker containers)                              shared backing services in infra:
   argocd ............ pulls deploy/** from your GitHub fork            keycloak, cosmos, rabbitmq, valkey,
   gatekeeper-system . rejects manifests that break the rules          aspire-dashboard
 registry localhost:5001 (outside the cluster): api, worker, cedar-policies images, opa-bundle artifacts
```

Contents: [1 Prerequisites](#1-prerequisites) · [2 First run](#2-first-run-from-a-fresh-clone) ·
[3 Use it](#3-use-it) · [4 Environments](#4-environments) · [5 Change things](#5-change-things) ·
[6 Guardrails](#6-guardrails-gatekeeper) · [7 Stop, rebuild, switch to Aspire](#7-stop-rebuild-switch-to-aspire) ·
[8 Troubleshooting](#8-troubleshooting) · [9 Reference](#9-reference) · [10 Tear down](#10-tear-down)

## 1. Prerequisites

- macOS or Linux, **Docker Desktop with at least 12 GB of memory** (the whole lab uses about 7 GB), host ports
  **80 and 443 free**.
- A GitHub account (Argo CD pulls from your fork).
- Tools: `brew install kind kubectl helm kustomize sops ksops age argocd oras gator jq`, plus the .NET SDK named in
  `global.json` and `python3`. Keep `gator` on the same minor version as the Gatekeeper chart (3.23).

```bash
deploy/bootstrap/check-tools.sh   # prints "tools: ok" or an install hint for each missing tool
```

macOS: sops looks for its key under `~/Library/Application Support`, so put this in your shell profile:

```bash
export SOPS_AGE_KEY_FILE=~/.config/sops/age/keys.txt
```

## 2. First run from a fresh clone

Every script is idempotent: re-running it on a finished step changes nothing.

### 2.1 Fork and clone

Fork this repository on GitHub and keep the fork **public** (Argo pulls anonymously; a private fork would need Argo
repository credentials, which this lab does not set up). Then:

```bash
git clone https://github.com/<you>/open-policy-demo.git && cd open-policy-demo
```

Argo follows the branch named in `targetRevision` in `deploy/argocd/` (`main`). Work on that branch, or change
`targetRevision` everywhere in `deploy/argocd/` to your branch.

### 2.2 Point Argo at your fork, and make your own secrets

The committed secrets are encrypted to the original author's key, so nobody else can decrypt them. Replace them:

```bash
deploy/bootstrap/use-fork.sh https://github.com/<you>/open-policy-demo.git   # every Application now reads your fork
deploy/bootstrap/new-secrets.sh   # age key (if none), .sops.yaml, fresh passwords, every deploy/**/secrets/*.sops.yaml
git add .sops.yaml deploy/argocd ':(glob)deploy/**/secrets/*.sops.yaml' && git commit -m "lab: my fork and secrets"
```

**Back up `~/.config/sops/age/keys.txt`.** Without it nobody can decrypt your secrets; with it, anyone can.
Stage only these paths: the fork is public, so a stray decrypted file (`sops -d ... > file`) must never be committed.

### 2.3 Cluster and local registry

```bash
deploy/bootstrap/cluster.sh   # ~1 min: kind cluster "lab" (kubectl context kind-lab), registry localhost:5001,
                              # CoreDNS rewrite; prints "cluster: ready"
kubectl --context kind-lab get nodes   # 3 nodes, all Ready
```

### 2.4 Build images and policies, pin them, push

The overlays pin image tags that exist only in the original author's registry, so build your own. Tags are the git
short SHA of `HEAD`, and the build scripts refuse to run with uncommitted changes in their inputs.

```bash
images=$(deploy/build/build-images.sh | tail -1)       # api and worker -> localhost:5001, ~2 min
policies=$(deploy/build/build-policies.sh | tail -1)   # opa test, then opa-bundle + cedar-policies -> localhost:5001
deploy/build/pin.sh dev staging prod --images "$images" --policies "$policies"
deploy/build/validate.sh                               # renders every overlay like Argo, checks Gatekeeper rules
git commit -am "lab: pin my builds" && git push
```

### 2.5 Argo CD

```bash
deploy/bootstrap/argocd-install.sh   # ~1 min: Argo CD + KSOPS, your age key as a Secret, the root Application
kubectl --context kind-lab -n argocd get applications -w   # wait until all are Synced/Healthy (3-5 min) except api-prod
```

Argo creates everything else from Git in waves: namespaces, then Envoy Gateway and Gatekeeper, then the Gateway and
policy templates, then infra and constraints, then the three app environments. `api-prod` stays **OutOfSync** because
prod is synced by hand: open the UI (section 3) and press **Sync** on `api-prod`, or run

```bash
kubectl --context kind-lab -n argocd patch application api-prod --type merge \
  -p '{"operation":{"initiatedBy":{"username":"me"},"sync":{"prune":true}}}'
```

### 2.6 Check it works

| Check | Expect |
|---|---|
| `kubectl --context kind-lab -n argocd get applications` | 12 Applications, all Synced and Healthy |
| `curl -s http://api.dev.localtest.me/buildinfo` | JSON with `"GitHash": "<your images tag>"` |
| `curl -s -o /dev/null -w '%{http_code}' http://dashboard.localtest.me/` | `302` (to the logs page) |
| E2E suite against dev (section 3) | all tests pass |

## 3. Use it

| What | Where |
|---|---|
| Api per environment | `http://api.dev.localtest.me`, `http://api.staging.localtest.me`, `http://api.prod.localtest.me` |
| Keycloak (tokens, admin console) | `http://keycloak.localtest.me` — admin password: `kubectl --context kind-lab -n infra get secret keycloak-admin -o jsonpath='{.data.password}' \| base64 -d` |
| Aspire Dashboard (logs and traces of every app pod) | `http://dashboard.localtest.me` (no login) |
| Argo CD UI | `kubectl --context kind-lab -n argocd port-forward svc/argocd-server 8443:443`, then `https://localhost:8443`, user `admin`, password `argocd admin initial-password -n argocd --kube-context kind-lab` |
| RabbitMQ management | `kubectl --context kind-lab -n infra port-forward svc/rabbitmq 15672`, then `http://localhost:15672`, user `admin`, password in Secret `infra/rabbitmq` key `admin-password` |

`*.localtest.me` resolves to 127.0.0.1 on any machine, and inside the cluster CoreDNS sends `keycloak.localtest.me`
to the Gateway too, so the laptop and the pods see the same token issuer.

Demo users and rules are in the [main README](../README.md#users) (password `Passw0rd!`). Unlike Aspire, each cluster
environment fills in a missing `X-Environment` header with its own name. `requests.http` has a commented block with
the cluster URLs.

```bash
tok=$(curl -s -d grant_type=password -d client_id=demo-api -d username=editor-acme -d 'password=Passw0rd!' \
  http://keycloak.localtest.me/realms/opa-demo/protocol/openid-connect/token | jq -r .access_token)
curl -s http://api.dev.localtest.me/documents -H "Authorization: Bearer $tok"
```

E2E suite against dev (tests that need the sidecars or the broker directly are skipped):

```bash
E2E_TARGET=k8s E2E_API_URL=http://api.dev.localtest.me E2E_KEYCLOAK_URL=http://keycloak.localtest.me \
  dotnet test --project tests/Tests --filter-namespace "Tests.E2E" --filter-not-trait "Target=aspire-only"
```

Every request is one trace in the dashboard (Traces): the api request, its OPA or Cedar call and Cosmos writes, then
in the worker the outbox relay, the RabbitMQ publish and the audit and cache handlers.

## 4. Environments

| | dev | staging | prod |
|---|---|---|---|
| URL | `api.dev.localtest.me` | `api.staging.localtest.me` | `api.prod.localtest.me` |
| Policy engine | OPA | Cedar | OPA |
| Accepted `X-Environment` (default) | dev, staging, prod (dev) | staging, prod (staging) | prod (prod) |
| api pods | 1 | 1 | 2 |
| Cosmos database / RabbitMQ vhost / Valkey DB | `policy-demo-dev` / `dev` / 0 | `policy-demo-staging` / `staging` / 1 | `policy-demo-prod` / `prod` / 2 |
| Argo sync | automatic, self-heal | automatic, self-heal | **manual** |

Each environment is a kustomize overlay of `deploy/apps/api/base`; only `overlays/<env>/` differs. In dev and staging
a hand edit in the cluster is reverted within seconds; in prod it is reported as OutOfSync and left alone.

## 5. Change things

Argo deploys only what is pushed. Run `deploy/build/validate.sh` before every push of a `deploy/` change: it renders
every overlay the way Argo does and runs the Gatekeeper tests (`gator verify`) and the rules against the rendered
manifests (`gator test`), so a violation shows up on your laptop instead of as a failed sync.

### Deploy an app or policy change to dev

```bash
git commit -am "..."                                   # builds tag HEAD, so commit first
deploy/build/pin.sh dev --images "$(deploy/build/build-images.sh | tail -1)"        # app code (src/)
deploy/build/pin.sh dev --policies "$(deploy/build/build-policies.sh | tail -1)"    # policies/
deploy/build/validate.sh && git commit -am "deploy(dev): ..." && git push           # dev rolls automatically
```

Change a policy in **both** `policies/authz.rego` and `policies/authz.cedar`: dev and prod use OPA, staging uses Cedar,
and they must give the same answers and reasons. A failing `opa test` stops `build-policies.sh` before anything is
pushed; invalid Cedar stops it with line numbers.

### Promote dev → staging → prod

A promotion is a commit that copies tags from one overlay to the next. Nothing is rebuilt, so prod runs exactly what
staging ran.

1. `deploy/build/promote.sh dev staging`, review the diff (tag lines only), commit, push. staging rolls automatically.
2. `deploy/build/promote.sh staging prod`, commit, push. `api-prod` turns **OutOfSync** in the Argo UI (App Diff
   shows the tags) and keeps serving the old version.
3. Press **Sync** on `api-prod`, tick **Prune**, **Synchronize**. It rolls one pod at a time and stays available.
   Prune matters: a changed ConfigMap gets a new hashed name and the old one leaves Git; a manual sync deletes it only
   when asked, otherwise prod stays OutOfSync ("requires pruning").

Check what an environment answers (403 with the current reason):

```bash
curl -s -X POST http://api.staging.localtest.me/documents -H "Authorization: Bearer $tok" \
  -H 'X-Environment: prod' -H 'Content-Type: application/json' -d '{"title":"x"}'
```

### Secrets

Files matching `deploy/**/secrets/*.sops.yaml` are encrypted to the age public key in `.sops.yaml`; only values are
encrypted, so diffs stay readable. Argo decrypts them with KSOPS using the private key that `argocd-install.sh`
copied into Secret `argocd/sops-age`. Never commit a decrypted file.

```bash
sops deploy/infra/secrets/keycloak-admin.sops.yaml   # opens decrypted in $EDITOR, re-encrypts on save
deploy/bootstrap/new-secrets.sh --rotate             # new passwords everywhere
```

Each environment's `connections` Secret gets a content-hashed name (`connections-<hash>`), so a rotation rolls that
environment's api and worker by itself (prod on its manual Sync, with Prune). RabbitMQ and Keycloak read their
secrets only at startup; after a rotation syncs, restart them so the new passwords take effect:
`kubectl --context kind-lab -n infra rollout restart statefulset/rabbitmq deployment/keycloak`.

## 6. Guardrails (Gatekeeper)

Gatekeeper is an admission webhook: the API server asks it before storing any object, and a denial means the object
never exists (the running version keeps running). Rules follow the namespace label `lab.dev/tier`:

| Constraint | infra | app-* |
|---|---|---|
| CPU/memory requests and limits on every container (sidecars included) | ✓ | ✓ |
| No `:latest`, no untagged image | ✓ | ✓ |
| Images only from `localhost:5001/` | – | ✓ |
| Labels `app.kubernetes.io/name` and `owner` | ✓ | ✓ |
| `replicas >= 2` (api Deployment) | – | `app-prod` |

The rules read Pods; an `ExpansionTemplate` makes Gatekeeper check the Pod a Deployment or StatefulSet would create,
so a bad Deployment fails the Argo sync with a message such as `[no-latest-or-untagged] [Implied by expand-workloads]
container <opa> uses a disallowed tag`. Templates are copied from `gatekeeper-library` (commit in each file); tests
and fixtures are in `deploy/platform/gatekeeper-policies/tests`. After changing the api base or the dev overlay, run
`deploy/build/validate.sh --refresh-fixture` so the "real dev pod" fixture matches.

## 7. Stop, rebuild, switch to Aspire

kind does not support stopping and restarting its node containers reliably (it froze Docker Desktop's VM once while
this lab was built). Delete the cluster instead; the registry, which holds your images, lives outside it.

```bash
kind delete cluster --name lab                 # stop; Aspire may run now
deploy/bootstrap/cluster.sh && deploy/bootstrap/argocd-install.sh   # back again, ~5 minutes until all Synced
```

After a rebuild Argo recreates everything from Git, then sync `api-prod` once by hand (section 2.5). Cosmos and
RabbitMQ data is gone (expected); Keycloak re-imports the realm. `kind create` makes `kind-lab` your current kubectl
context; Docker Desktop may switch it to `docker-desktop` when it restarts, which is why every command here passes
`--context kind-lab`.

## 8. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `kubectl get applications` errors or shows another cluster | current context is `docker-desktop` (Docker Desktop restarted) | pass `--context kind-lab`, or `kubectl config use-context kind-lab` |
| `sops: failed to get the data key` / `no identity matched` | sops cannot find the key (macOS default path) | `export SOPS_AGE_KEY_FILE=~/.config/sops/age/keys.txt`; on a clone run `new-secrets.sh` |
| Argo app shows `ComparisonError` mentioning ksops or `secrets/...` | KSOPS cannot decrypt: wrong or missing `sops-age` Secret | re-run `deploy/bootstrap/argocd-install.sh` (re-seeds the key from your laptop) |
| A change never reaches the cluster | not pushed, pushed to another branch, or the fork is private | push to the branch in `targetRevision`; keep the fork public |
| Sync fails with `admission webhook "validation.gatekeeper.sh" denied` | a manifest breaks a guardrail; the message names the rule | fix the manifest; `validate.sh` shows the same message locally |
| Pod `ImagePullBackOff` on `localhost:5001/...:<tag>` | that tag was never built in your registry | build it and `pin.sh` the tag (section 5) |
| api pod `CrashLoopBackOff`, log `Deployment:AllowedEnvironments contains unknown environment` | invalid allowlist in the overlay (by design the api refuses to start) | fix `Deployment__AllowedEnvironments__N` in `overlays/<env>/kustomization.yaml` (one value per entry) |
| api or OPA briefly 2/3 Ready after a sync | several pods starting at once miss the 1 s readiness probes | wait about a minute; readiness holds traffic back meanwhile |
| `api-prod` OutOfSync after a sync, ConfigMap "requires pruning" | manual sync without Prune | sync again with **Prune** |
| `kubectl exec` hangs, pods stuck mid-start, `Hangcheck` in `docker exec lab-worker dmesg` | Docker Desktop VM stalled (after `docker stop` of the nodes) | restart Docker Desktop; avoid stopping the nodes (section 7) |
| `kind delete cluster` fails: `could not kill container ... did not receive an exit event` | Docker Desktop's daemon is stuck | restart Docker Desktop, then run `kind delete cluster --name lab` again |
| `curl http://*.localtest.me` connects but hangs | Envoy's Service would only answer on nodes running Envoy | fixed in `deploy/platform/gateway/envoy-proxy.yaml` (`externalTrafficPolicy: Cluster`); check it was not reverted |
| `deploy/build/validate.sh` never finishes when run from a wrapper | `gator` waits on an open stdin | fixed (`</dev/null`); keep it when editing the script |
| Build script: `refusing to tag ... uncommitted changes` | the tag would not describe the content | commit, or `ALLOW_DIRTY=1` for a throwaway `<sha>-dirty` build |

Useful views: `kubectl --context kind-lab -n <ns> get pods`, `kubectl ... describe pod <pod>` (Events at the end),
`kubectl ... logs <pod> -c <container>`, and `kubectl --context kind-lab get constraints` (Gatekeeper audit counts).

## 9. Reference

### What runs where

- **kind cluster `lab`:** one control-plane and two worker nodes, each a Docker container. The control plane publishes
  node ports 30080/30443 on `127.0.0.1:80/443`; Envoy's Service `envoy-lab` listens there.
- **Registry `kind-registry`:** `localhost:5001` from the laptop; nodes pull `localhost:5001/...` through a containerd
  mirror to `http://kind-registry:5000`. Pods that call the registry directly (OPA fetching its bundle) use
  `kind-registry:5000`.
- **CoreDNS:** rewrites `keycloak.localtest.me` to the Envoy Gateway Service.
- **Api pod:** `api` container plus OPA and Cedar as native sidecars (init containers with `restartPolicy: Always`)
  bound to `127.0.0.1`, so only the api can call them. OPA loads `opa-bundle:<tag>` from the registry as an OCI
  artifact; Cedar's policies are baked into `cedar-policies:<tag>`.

### Scripts

| Script | Does |
|---|---|
| `deploy/bootstrap/check-tools.sh` | checks the required tools |
| `deploy/bootstrap/use-fork.sh <url>` | points every Application at your fork |
| `deploy/bootstrap/new-secrets.sh [--rotate]` | your age key and fresh secrets |
| `deploy/bootstrap/cluster.sh` | kind cluster, registry, CoreDNS rewrite |
| `deploy/bootstrap/argocd-install.sh` | Argo CD + KSOPS, age key Secret, root Application |
| `deploy/build/build-images.sh` | builds and pushes `api` and `worker`, prints the tag |
| `deploy/build/build-policies.sh` | tests and pushes the OPA bundle and Cedar image, mirrors OPA, prints the tag |
| `deploy/build/pin.sh <env>... --images T --policies T` | writes built tags into overlays |
| `deploy/build/promote.sh <from> <to>` | copies tags from one overlay to the next |
| `deploy/build/validate.sh [--refresh-fixture]` | renders overlays, runs the Gatekeeper tests and rules |

### Layout

```
deploy/bootstrap/   imperative setup (the only non-GitOps step)
deploy/build/       build, pin, promote, validate
deploy/argocd/      root Application and one Application per component or environment
deploy/platform/    namespaces, Argo values, Envoy Gateway and Gateway, Gatekeeper templates/constraints/tests
deploy/infra/       keycloak, cosmos, rabbitmq, valkey, dashboard, and their SOPS secrets
deploy/apps/api/    base (api + worker) and overlays/{dev,staging,prod}
```

## 10. Tear down

```bash
kind delete cluster --name lab && docker rm -f kind-registry   # removes the cluster and every built image
```
