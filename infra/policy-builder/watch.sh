#!/bin/sh
# Rebuilds the OPA bundle whenever a .rego file changes. A failing `opa test` keeps the previous bundle.
set -u

POLICY_DIR="${POLICY_DIR:-/policies}"
BUNDLE_DIR="${BUNDLE_DIR:-/bundles}"
INTERVAL_SECONDS="${INTERVAL_SECONDS:-2}"
last=""

while true; do
  current="$(cat "$POLICY_DIR"/*.rego 2>/dev/null | md5sum)"
  if [ "$current" != "$last" ]; then
    last="$current"
    echo "[policy-builder] change detected, running opa test"
    if opa test "$POLICY_DIR"; then
      if opa build --bundle "$POLICY_DIR" --ignore '*_test.rego' --output "$BUNDLE_DIR/bundle.tar.gz.tmp"; then
        mv "$BUNDLE_DIR/bundle.tar.gz.tmp" "$BUNDLE_DIR/bundle.tar.gz"
        echo "[policy-builder] bundle published"
      else
        echo "[policy-builder] opa build failed; previous bundle kept"
      fi
    else
      echo "[policy-builder] opa test failed; previous bundle kept"
    fi
  fi
  sleep "$INTERVAL_SECONDS"
done
