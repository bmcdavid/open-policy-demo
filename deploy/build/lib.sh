# Shared helpers for the build scripts. Source it; do not run it.

# Prints the tag for an artifact built from the given paths: the short SHA of HEAD.
# Refuses when any of those paths has uncommitted or untracked changes, because the SHA would then lie about the
# content. ALLOW_DIRTY=1 builds anyway and appends -dirty, so such a tag can never be mistaken for a commit.
artifact_tag() {
  local sha dirty
  sha="$(git rev-parse --short HEAD)"
  dirty="$(git status --porcelain -- "$@")"
  if [ -z "$dirty" ]; then
    echo "$sha"
  elif [ "${ALLOW_DIRTY:-}" = 1 ]; then
    echo "$sha-dirty"
  else
    {
      echo "refusing to tag $sha: uncommitted changes in the build inputs (commit them, or set ALLOW_DIRTY=1):"
      echo "$dirty"
    } >&2
    return 1
  fi
}
