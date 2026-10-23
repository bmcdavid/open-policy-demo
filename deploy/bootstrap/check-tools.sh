#!/usr/bin/env bash
# Verifies the CLI tools the lab needs are on PATH; prints an install hint for each missing one.
set -euo pipefail

hint() {
  case "$1" in
    docker) echo "install Docker Desktop (12 GB memory)" ;;
    dotnet) echo "install the .NET SDK version in global.json" ;;
    python3) echo "xcode-select --install (or brew install python)" ;;
    gator) echo "brew install gator (keep it on the same minor version as the pinned Gatekeeper chart)" ;;
    *) echo "brew install $1" ;;
  esac
}

missing=0
for tool in git docker kind kubectl helm kustomize sops ksops age argocd oras gator dotnet python3 jq; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "missing: $tool — $(hint "$tool")" >&2
    missing=1
  fi
done

if [ "$missing" -ne 0 ]; then
  exit 1
fi
echo "tools: ok"
