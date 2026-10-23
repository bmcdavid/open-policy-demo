#!/usr/bin/env bash
# use-fork.sh https://github.com/<you>/<repo>.git
# Argo CD pulls every manifest from GitHub, so a clone must deploy from its own fork. This points every Application
# that reads this repo (repoURL on github.com) at the fork, and leaves Helm chart sources alone. Idempotent.
# The fork must be public: Argo pulls anonymously (a private fork needs Argo repository credentials).
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

fork="${1:-}"
if ! [[ "$fork" =~ ^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
  echo "usage: use-fork.sh https://github.com/<you>/<repo>.git" >&2
  exit 2
fi
[[ "$fork" == *.git ]] || fork="$fork.git"

changed=0
while IFS= read -r file; do
  if grep -Eq "^    repoURL: https://github\.com/" "$file" && ! grep -qx "    repoURL: $fork" "$file"; then
    sed -i.bak -E "s#^    repoURL: https://github\.com/.*#    repoURL: $fork#" "$file" && rm "$file.bak"
    echo "updated $file"
    changed=$((changed + 1))
  fi
done < <(find deploy/argocd -name '*.yaml' | sort)

echo "use-fork: $changed file(s) now point at $fork; commit and push them to the fork before bootstrapping."
