#!/usr/bin/env bash
# Run before pushing. Renders every overlay and deploy/infra the way Argo does, then checks Gatekeeper twice:
#   gator verify: the constraint test suite (one passing and one failing fixture per constraint)
#   gator test:   the rendered manifests themselves, with workloads expanded to Pods as in the cluster
# --refresh-fixture rewrites the real dev api pod fixture after a base or dev overlay change.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
export SOPS_AGE_KEY_FILE="${SOPS_AGE_KEY_FILE:-$HOME/.config/sops/age/keys.txt}"

policies=deploy/platform/gatekeeper-policies
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Same flags as Argo's repo server. Decrypted Secrets are dropped before anything is written to disk.
render() {
  kustomize build --enable-alpha-plugins --enable-exec --load-restrictor LoadRestrictionsNone "$1" |
    awk -v RS='\n---\n' -v ORS='\n---\n' '!/(^|\n)kind: Secret\n/'
}

targets=(deploy/infra deploy/apps/api/overlays/*)
for target in "${targets[@]}"; do
  render "$target" > "$work/$(tr / _ <<<"$target").yaml"
  echo "rendered $target"
done

if [ "${1:-}" = --refresh-fixture ]; then
  awk -v RS='\n---\n' '/\nkind: Deployment\n/ && /\n  name: api\n/' "$work/deploy_apps_api_overlays_dev.yaml" > "$work/api.yaml"
  {
    echo "# Generated: the dev overlay's api Deployment expanded to its Pod (see deploy/build/validate.sh for the commands)."
    echo "# Regenerate after changing the base or dev overlay: deploy/build/validate.sh --refresh-fixture"
    gator expand --filename "$work/api.yaml" --filename "$policies/templates/expand-workloads.yaml" --format yaml
  } > "$policies/tests/fixtures/api-pod-dev.yaml"
  echo "refreshed $policies/tests/fixtures/api-pod-dev.yaml"
fi

gator verify "$policies/tests"

kustomize build "$policies/templates" > "$work/policy-templates.yaml"
kustomize build "$policies/constraints" > "$work/policy-constraints.yaml"
gator test --filename "$work" --filename deploy/platform/namespaces/namespaces.yaml
echo "validate: ok"
