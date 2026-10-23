#!/usr/bin/env bash
# Phase 1 bootstrap: kind cluster "lab" (kubectl context kind-lab), local registry, CoreDNS rewrite.
# Idempotent: re-running on an existing cluster changes nothing.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
context=kind-lab

"$here/check-tools.sh"

if ! kind get clusters | grep -qx lab; then
  kind create cluster --config "$here/kind-config.yaml"
fi

"$here/registry.sh"

# Restart CoreDNS only when the Corefile actually changed, so a re-run does not churn its pods.
result="$(kubectl --context "$context" apply -f "$here/coredns-patch.yaml")"
if ! grep -q ' unchanged$' <<<"$result"; then
  echo "$result"
  kubectl --context "$context" -n kube-system rollout restart deployment/coredns
  kubectl --context "$context" -n kube-system rollout status deployment/coredns --timeout=120s
fi

kubectl --context "$context" wait --for=condition=Ready nodes --all --timeout=180s >/dev/null
echo "cluster: ready"
