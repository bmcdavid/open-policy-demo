# Kubernetes lab

Runs this repo's authorization demo on a local kind cluster, managed by Argo CD from Git.
Design: [`docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md`](../docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md).

> **Never run `aspire run` while the cluster is up.** Both use ports 8080, 8181 and 8180, and together
> they exceed Docker Desktop's memory. Stop one before starting the other (`kind delete cluster --name lab`).

## Prerequisites

- Docker Desktop with 12 GB memory.
- `brew install kind helm kustomize sops ksops age argocd oras gator` (plus `kubectl` and the .NET SDK in `global.json`).
  `deploy/bootstrap/check-tools.sh` reports anything missing.

## Bootstrap from nothing

Every step is idempotent; re-running it on an existing cluster changes nothing.

```bash
deploy/bootstrap/cluster.sh        # kind cluster "lab" (context kind-lab), registry localhost:5001, CoreDNS rewrite
deploy/bootstrap/argocd-install.sh # Argo CD + KSOPS, age key Secret, root Application
```

Check it:

```bash
kubectl get nodes                       # 3 nodes, all Ready
kubectl -n argocd get applications      # every Application Synced and Healthy
```

`argocd-install.sh` needs the age key at `~/.config/sops/age/keys.txt` (`age-keygen -o ~/.config/sops/age/keys.txt`)
and Argo pulls from the pushed `k8s` branch, so push before expecting a change in the cluster.

## Argo CD UI

```bash
argocd admin initial-password -n argocd                # user: admin
kubectl -n argocd port-forward svc/argocd-server 8443:443
```

Open <https://localhost:8443> (self-signed certificate).

## Secrets (SOPS + age)

Files matching `deploy/**/secrets/*.sops.yaml` are encrypted with the age public key in `.sops.yaml`. Argo decrypts
them with KSOPS using the private key that `argocd-install.sh` copied into Secret `argocd/sops-age`.

On macOS, sops looks for the key under `~/Library/Application Support`, so point it at the lab key first:

```bash
export SOPS_AGE_KEY_FILE=~/.config/sops/age/keys.txt
sops deploy/infra/secrets/smoke.sops.yaml   # edit decrypted; re-encrypts on save
```

Never commit a decrypted file.

## What runs where

- **kind cluster `lab`:** one control-plane and two worker nodes, each a Docker container. The control plane publishes
  node ports 30080/30443 on `127.0.0.1:80/443`.
- **Registry `kind-registry`:** `localhost:5001` from the laptop; nodes pull `localhost:5001/...` through a containerd
  mirror to `http://kind-registry:5000`. Pods that talk to the registry directly (OPA bundles) use `kind-registry:5000`.
- **CoreDNS:** rewrites `keycloak.localtest.me` to the Envoy Gateway Service, so pods and the laptop use the same
  Keycloak URL (and the token issuer matches).

## Environments

| | dev | staging | prod |
|---|---|---|---|
| URL | `api.dev.localtest.me` | `api.staging.localtest.me` | `api.prod.localtest.me` |
| Policy engine | OPA | Cedar | OPA |
| Accepted `X-Environment` (default) | dev, staging, prod (dev) | staging, prod (staging) | prod (prod) |
| api pods | 1 | 1 | 2 |
| Argo sync | automatic, self-heal | automatic, self-heal | **manual** |

Each environment is a kustomize overlay of `deploy/apps/api/base`; only the overlay differs.

## Promote a policy change

A promotion is a Git commit that copies tags from one overlay to the next. Nothing is rebuilt along the way, so prod
runs exactly the artifacts that were tested in staging.

1. Change the policy in **both** `policies/authz.rego` and `policies/authz.cedar` (they must give the same reasons),
   commit, then build the artifacts and point dev at them:

   ```bash
   tag=$(deploy/build/build-policies.sh | tail -1)    # opa test gates it; pushes opa-bundle:<tag> and cedar-policies:<tag>
   # set <tag> as newTag of localhost:5001/cedar-policies in overlays/dev/kustomization.yaml
   # and in the opa-bundle resource line of overlays/dev/opa-config.yaml
   deploy/build/validate.sh && git commit -am "deploy(dev): policies $tag" && git push
   ```

   dev rolls automatically.
2. `deploy/build/promote.sh dev staging`, review the diff (tag lines only), commit, push. staging rolls automatically;
   it runs Cedar, so the change shows in its 403 reasons.
3. `deploy/build/promote.sh staging prod`, commit, push. `api-prod` turns **OutOfSync** in the Argo UI, showing the
   tag diff, and keeps serving the old version. Press **Sync** on `api-prod`; it rolls one pod at a time.

Check any environment:

```bash
tok=$(curl -s -d grant_type=password -d client_id=demo-api -d username=editor-acme -d 'password=Passw0rd!' \
  http://keycloak.localtest.me/realms/opa-demo/protocol/openid-connect/token | jq -r .access_token)
curl -s -X POST http://api.prod.localtest.me/documents -H "Authorization: Bearer $tok" \
  -H 'X-Environment: prod' -H 'Content-Type: application/json' -d '{"title":"x"}'   # 403 with the current reason
```

App images follow the same path: `deploy/build/build-images.sh` prints the tag for `api` and `worker`.

## Before pushing a manifest change

```bash
deploy/build/validate.sh   # renders every overlay like Argo, then gator verify (policy tests) + gator test (manifests)
```

## Guardrails (Gatekeeper)

Gatekeeper rejects objects at admission, before they are stored. Rules follow the namespace label `lab.dev/tier`:

| Constraint | infra | app-* |
|---|---|---|
| CPU/memory requests and limits on every container (sidecars included) | ✓ | ✓ |
| No `:latest`, no untagged image | ✓ | ✓ |
| Images only from `localhost:5001/` | – | ✓ |
| Labels `app.kubernetes.io/name` and `owner` | ✓ | ✓ |
| `replicas >= 2` (api Deployment) | – | `app-prod` |

The checks read Pods; an `ExpansionTemplate` makes Gatekeeper check the Pod a Deployment or StatefulSet would create,
so a bad Deployment fails the Argo sync with the constraint message instead of failing silently later. Templates are
copied from `gatekeeper-library` (commit in each file); tests are in `deploy/platform/gatekeeper-policies/tests`.

## Tear down

```bash
kind delete cluster --name lab && docker rm -f kind-registry
```
