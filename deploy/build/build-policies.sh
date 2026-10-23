#!/usr/bin/env bash
# Policies are versioned artifacts, tagged with the git short SHA of their inputs:
#   OPA:   opa test (a failure stops everything), opa build, push as OCI artifact localhost:5001/opa-bundle:<tag>
#   Cedar: CedarPolicyTool validates and generates policies.json, baked into localhost:5001/cedar-policies:<tag>
# Also mirrors the pinned OPA image to localhost:5001/opa, because app pods may only pull from the local registry.
# An overlay pins these tags, so promoting a policy is a tag change in Git. Prints the tag as its last line.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
source deploy/build/lib.sh

opa_version=1.21.4
opa_image="openpolicyagent/opa:$opa_version"
registry=localhost:5001

tag="$(artifact_tag policies src/CedarPolicyTool)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "opa test" >&2
docker run --rm -v "$PWD/policies:/policies:ro" "$opa_image" test /policies >&2

docker run --rm -v "$PWD/policies:/policies:ro" -v "$work:/out" "$opa_image" \
  build --bundle /policies --ignore '*_test.rego' --revision "$tag" --output /out/bundle.tar.gz >&2
# An OCI artifact is any content stored in a registry under a manifest, not only images. OPA pulls the single layer.
(cd "$work" && oras push --plain-http "$registry/opa-bundle:$tag" \
  bundle.tar.gz:application/vnd.oci.image.layer.v1.tar+gzip >&2)

mkdir "$work/cedar"
dotnet run --project src/CedarPolicyTool -- generate policies/authz.cedar "$work/cedar/policies.json" >&2
cp deploy/build/cedar-policies/Dockerfile "$work/cedar/"
docker build -q -t "$registry/cedar-policies:$tag" "$work/cedar" >&2
docker push -q "$registry/cedar-policies:$tag" >&2

if ! curl -sf "http://$registry/v2/opa/manifests/$opa_version" -H 'Accept: application/vnd.oci.image.index.v1+json' >/dev/null; then
  docker pull -q "$opa_image" >&2
  docker tag "$opa_image" "$registry/opa:$opa_version"
  docker push -q "$registry/opa:$opa_version" >&2
fi

echo "$tag"
