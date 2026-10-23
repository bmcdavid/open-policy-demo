#!/usr/bin/env bash
# Phase 2 bootstrap: installs Argo CD with KSOPS, seeds the age key, and applies the root Application.
# After this, everything else in the cluster comes from Git. Idempotent.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
deploy="$(dirname "$here")"
context=kind-lab
chart_version=10.11.0
age_key="${SOPS_AGE_KEY_FILE:-$HOME/.config/sops/age/keys.txt}"

if [ ! -f "$age_key" ]; then
  echo "missing age key at $age_key (age-keygen -o $age_key)" >&2
  exit 1
fi

kubectl --context "$context" create namespace argocd --dry-run=client -o yaml | kubectl --context "$context" apply -f - >/dev/null

# The repo server mounts this Secret, so it must exist before the chart is installed.
kubectl --context "$context" -n argocd create secret generic sops-age --from-file=keys.txt="$age_key" \
  --dry-run=client -o yaml | kubectl --context "$context" apply -f - >/dev/null

helm repo add argo https://argoproj.github.io/argo-helm >/dev/null 2>&1 || true
helm upgrade --install argocd argo/argo-cd --kube-context "$context" --namespace argocd \
  --version "$chart_version" --values "$deploy/platform/argocd/values.yaml" --wait --timeout 10m >/dev/null

kubectl --context "$context" -n argocd rollout status deployment/argocd-repo-server --timeout=180s >/dev/null
kubectl --context "$context" apply -f "$deploy/argocd/root-app.yaml" >/dev/null
echo "argocd: ready"
