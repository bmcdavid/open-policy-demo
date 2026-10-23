# Kubernetes lab

Runs this repo's authorization demo on a local kind cluster, managed by Argo CD from Git.
Design: [`docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md`](../docs/superpowers/specs/2026-10-23-k8s-gitops-lab-design.md).

> **Never run `aspire run` while the cluster is up.** Both use ports 8080, 8181 and 8180, and together
> they exceed Docker Desktop's memory. Stop one before starting the other (`kind delete cluster --name lab`).

## Prerequisites

- Docker Desktop with 12 GB memory.
- `brew install kind helm kustomize sops age argocd oras gator` (plus `kubectl` and the .NET SDK in `global.json`).
  `deploy/bootstrap/check-tools.sh` reports anything missing.

## Bootstrap from nothing

Every step is idempotent; re-running it on an existing cluster changes nothing.

```bash
deploy/bootstrap/cluster.sh   # kind cluster "lab" (context kind-lab), registry localhost:5001, CoreDNS rewrite
```

Check it:

```bash
kubectl get nodes   # 3 nodes, all Ready
```

## What runs where

- **kind cluster `lab`:** one control-plane and two worker nodes, each a Docker container. The control plane publishes
  node ports 30080/30443 on `127.0.0.1:80/443`.
- **Registry `kind-registry`:** `localhost:5001` from the laptop; nodes pull `localhost:5001/...` through a containerd
  mirror to `http://kind-registry:5000`. Pods that talk to the registry directly (OPA bundles) use `kind-registry:5000`.
- **CoreDNS:** rewrites `keycloak.localtest.me` to the Envoy Gateway Service, so pods and the laptop use the same
  Keycloak URL (and the token issuer matches).

## Tear down

```bash
kind delete cluster --name lab && docker rm -f kind-registry
```
