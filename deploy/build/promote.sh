#!/usr/bin/env bash
# promote.sh <from-env> <to-env>: copies the image tags (kustomization.yaml images:) and the OPA bundle tag
# (opa-config.yaml) from one overlay to the next, and nothing else; then shows the diff. It does not commit:
# the promotion is the commit you make, and for prod the Sync you press in Argo.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
overlays="${OVERLAYS:-deploy/apps/api/overlays}"

usage() {
  echo "usage: promote.sh <from-env> <to-env>   (envs: $(ls "$overlays" | tr '\n' ' '))" >&2
  exit 2
}

[ $# -eq 2 ] || usage
from="$1" to="$2"
[ "$from" != "$to" ] || usage
[ -f "$overlays/$from/kustomization.yaml" ] && [ -f "$overlays/$to/kustomization.yaml" ] || usage

python3 -I - "$overlays/$from" "$overlays/$to" <<'PY'
import re, sys

src, dst = sys.argv[1], sys.argv[2]
image = re.compile(r"^  - name: (\S+)\n    newTag: (\S+)$", re.M)
bundle = re.compile(r"(resource: \S+/opa-bundle:)(\S+)")

def read(path):
    with open(path) as f:
        return f.read()

def write(path, text):
    with open(path, "w") as f:
        f.write(text)

tags = dict(image.findall(read(f"{src}/kustomization.yaml")))
target = read(f"{dst}/kustomization.yaml")
missing = set(tags) - {name for name, _ in image.findall(target)}
if missing:
    sys.exit(f"{dst}/kustomization.yaml has no images entry for {', '.join(sorted(missing))}")
write(f"{dst}/kustomization.yaml", image.sub(lambda m: f"  - name: {m[1]}\n    newTag: {tags.get(m[1], m[2])}", target))

source_bundle = bundle.search(read(f"{src}/opa-config.yaml"))
if source_bundle is None:
    sys.exit(f"{src}/opa-config.yaml has no opa-bundle resource")
write(f"{dst}/opa-config.yaml", bundle.sub(lambda m: m[1] + source_bundle[2], read(f"{dst}/opa-config.yaml")))
PY

git --no-pager diff -- "$overlays/$to" 2>/dev/null || true
