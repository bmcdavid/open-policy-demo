#!/usr/bin/env bash
# pin.sh <env>... [--images TAG] [--policies TAG]
# Writes freshly built tags into overlays: --images sets api and worker (build-images.sh output), --policies sets
# cedar-policies and the OPA bundle (build-policies.sh output). Changes only those lines, then shows the diff; it does
# not commit. Use it for dev after a build, or for every env when bootstrapping a fresh clone. Promotion between envs
# is promote.sh.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
overlays="${OVERLAYS:-deploy/apps/api/overlays}"

usage() {
  echo "usage: pin.sh <env>... [--images TAG] [--policies TAG]   (envs: $(ls "$overlays" | tr '\n' ' '))" >&2
  exit 2
}

# An explicit empty tag (for example "$(build-images.sh | tail -1)" after a failed build) is an error, not "skip".
envs=() images="" policies=""
while [ $# -gt 0 ]; do
  case "$1" in
    --images) [ $# -ge 2 ] && [ -n "$2" ] || usage; images="$2"; shift 2 ;;
    --policies) [ $# -ge 2 ] && [ -n "$2" ] || usage; policies="$2"; shift 2 ;;
    -*) usage ;;
    *) envs+=("$1"); shift ;;
  esac
done
[ ${#envs[@]} -gt 0 ] && { [ -n "$images" ] || [ -n "$policies" ]; } || usage
for tag in "$images" "$policies"; do
  [ -z "$tag" ] || [[ "$tag" =~ ^[A-Za-z0-9_.-]+$ ]] || usage
done
for env in "${envs[@]}"; do
  [ -f "$overlays/$env/kustomization.yaml" ] || usage
done

for env in "${envs[@]}"; do
  python3 -I - "$overlays/$env" "$images" "$policies" <<'PY'
import re, sys

overlay, images, policies = sys.argv[1:]
wanted = {}
if images:
    wanted.update({"localhost:5001/api": images, "localhost:5001/worker": images})
if policies:
    wanted["localhost:5001/cedar-policies"] = policies

image = re.compile(r"^  - name: (\S+)\n    newTag: (\S+)$", re.M)
path = f"{overlay}/kustomization.yaml"
with open(path) as f:
    text = f.read()
missing = set(wanted) - {name for name, _ in image.findall(text)}
if missing:
    sys.exit(f"{path} has no images entry for {', '.join(sorted(missing))}")
with open(path, "w") as f:
    f.write(image.sub(lambda m: f"  - name: {m[1]}\n    newTag: {wanted.get(m[1], m[2])}", text))

if policies:
    path = f"{overlay}/opa-config.yaml"
    with open(path) as f:
        text = f.read()
    text, count = re.subn(r"(resource: \S+/opa-bundle:)\S+", lambda m: m[1] + policies, text)
    if count != 1:
        sys.exit(f"{path} has no opa-bundle resource")
    with open(path, "w") as f:
        f.write(text)
PY
done

git --no-pager diff -- "$overlays" 2>/dev/null || true
