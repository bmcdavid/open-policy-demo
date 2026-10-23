#!/usr/bin/env bash
# Builds the Api and Worker container images with the .NET SDK (no Dockerfile) and pushes them to the local registry
# as localhost:5001/{api,worker}:<git short SHA>. An overlay pins that tag, so a pod runs exactly that commit.
# Prints the tag as its last line.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"
source deploy/build/lib.sh

tag="$(artifact_tag src Directory.Build.props Directory.Packages.props global.json)"
branch="$(git branch --show-current)"

for project in Api Worker; do
  repository="$(tr '[:upper:]' '[:lower:]' <<<"$project")"
  # GIT_HASH and GIT_BRANCH feed Core.BuildInfoAttribute, which /buildinfo reports.
  GIT_HASH="$tag" GIT_BRANCH="$branch" dotnet publish "src/$project" -c Release -t:PublishContainer \
    -p:ContainerRegistry=localhost:5001 -p:ContainerRepository="$repository" -p:ContainerImageTag="$tag" >&2
done

echo "$tag"
