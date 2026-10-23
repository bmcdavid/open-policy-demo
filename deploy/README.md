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
