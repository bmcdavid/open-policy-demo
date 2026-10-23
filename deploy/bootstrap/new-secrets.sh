#!/usr/bin/env bash
# new-secrets.sh [--rotate]
# Gives a clone its own secrets: creates an age key if there is none, makes .sops.yaml encrypt to it, and generates
# every deploy/**/secrets/*.sops.yaml with fresh passwords (Keycloak admin, one RabbitMQ user per environment plus
# admin, each environment's connection strings). Plaintext only ever exists in a private temp directory.
# Refuses to replace secrets the key can already read, unless --rotate is given.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

rotate=0
case "${1:-}" in
  "") ;;
  --rotate) rotate=1 ;;
  *) echo "usage: new-secrets.sh [--rotate]" >&2; exit 2 ;;
esac

key_file="${SOPS_AGE_KEY_FILE:-$HOME/.config/sops/age/keys.txt}"
if [ ! -f "$key_file" ]; then
  mkdir -p "$(dirname "$key_file")"
  age-keygen -o "$key_file" 2>/dev/null
  chmod 600 "$key_file"
  echo "created age key $key_file (back it up: without it nobody can decrypt these secrets)"
fi
export SOPS_AGE_KEY_FILE="$key_file"
public_key="$(age-keygen -y "$key_file")"

if [ "$rotate" -eq 0 ]; then
  while IFS= read -r file; do
    if sops -d "$file" >/dev/null 2>&1; then
      echo "refusing: $file already decrypts with $key_file, so these are your secrets; pass --rotate to replace them" >&2
      exit 1
    fi
  done < <(git ls-files ':(glob)deploy/**/secrets/*.sops.yaml')
fi

sed -i.bak -E "s#^    age: .*#    age: $public_key#" .sops.yaml && rm .sops.yaml.bak

envs=()
for dir in deploy/apps/api/overlays/*/; do
  envs+=("$(basename "$dir")")
done

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
chmod 700 "$work"

python3 -I - "$work" "${envs[@]}" <<'PY'
import base64, hashlib, json, os, secrets, sys

out, envs = sys.argv[1], sys.argv[2:]
# Valkey DB index per environment; a new environment needs an entry here.
valkey_db = {"dev": 0, "staging": 1, "prod": 2}
unknown = [e for e in envs if e not in valkey_db]
if unknown:
    sys.exit(f"no Valkey DB index for {', '.join(unknown)}; add it to valkey_db in new-secrets.sh")

# The Cosmos emulator's published, well-known key (the vNext emulator does not enforce it).
cosmos = ("AccountEndpoint=http://cosmos.infra:8081/;"
          "AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==")


def password():
    return secrets.token_urlsafe(18)


def rabbit_hash(pw):
    salt = os.urandom(4)
    return base64.b64encode(salt + hashlib.sha256(salt + pw.encode()).digest()).decode()


def secret(path, comment, name, namespace, data):
    lines = [f"# {line}" for line in comment] + [
        "apiVersion: v1", "kind: Secret", "metadata:", f"  name: {name}", f"  namespace: {namespace}",
        "type: Opaque", "stringData:"]
    for key, value in data.items():
        if "\n" in value:
            lines.append(f"  {key}: |")
            lines += [f"    {v}" for v in value.splitlines()]
        else:
            lines.append(f"  {key}: {value}")
    full = os.path.join(out, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w") as f:
        f.write("\n".join(lines) + "\n")


secret("deploy/infra/secrets/smoke.sops.yaml",
       ["Proves KSOPS decryption works in Argo: the cluster should hold value=decrypted."],
       "sops-smoke", "infra", {"value": "decrypted"})

secret("deploy/infra/secrets/keycloak-admin.sops.yaml",
       ["Keycloak bootstrap admin (console at http://keycloak.localtest.me/admin). Read it with:",
        "  kubectl -n infra get secret keycloak-admin -o jsonpath='{.data.password}' | base64 -d"],
       "keycloak-admin", "infra", {"username": "admin", "password": password()})

secret("deploy/infra/secrets/cosmos.sops.yaml",
       ["Connection string for the Cosmos emulator in infra (the emulator's well-known key; vNext does not enforce it).",
        "Secrets are per namespace, so each app overlay carries its own copy in its connections Secret."],
       "cosmos", "infra", {"connection-string": cosmos})

users = ["admin"] + [f"app-{e}" for e in envs]
passwords = {u: password() for u in users}
definitions = {
    "users": [{"name": u, "password_hash": rabbit_hash(passwords[u]),
               "hashing_algorithm": "rabbit_password_hashing_sha256",
               "tags": ["administrator"] if u == "admin" else []} for u in users],
    "vhosts": [{"name": e} for e in envs],
    "permissions": [{"user": f"app-{e}", "vhost": e, "configure": ".*", "write": ".*", "read": ".*"} for e in envs]
                   + [{"user": "admin", "vhost": e, "configure": ".*", "write": ".*", "read": ".*"} for e in envs],
}
secret("deploy/infra/secrets/rabbitmq.sops.yaml",
       [f"RabbitMQ definitions loaded at boot: vhosts {'/'.join(envs)}; users {', '.join(users[1:])} (each with",
        "full permissions on its own vhost only) and admin (administrator, all vhosts). Plain passwords are kept here too",
        "so the per-env connection strings (app overlays) can be written from them."],
       "rabbitmq", "infra",
       {"definitions.json": json.dumps(definitions, indent=2), **{f"{u}-password": passwords[u] for u in users}})

for e in envs:
    secret(f"deploy/apps/api/overlays/{e}/secrets/connections.sops.yaml",
           [f"Connection strings for app-{e}, read by the api and worker as ConnectionStrings__<name> env vars.",
            f"cosmos: the infra emulator (database chosen by Cosmos__Database); rabbitmq: user app-{e} on vhost {e};",
            f"valkey: DB index {valkey_db[e]}."],
           "connections", f"app-{e}",
           {"ConnectionStrings__cosmos": cosmos,
            "ConnectionStrings__rabbitmq": f"amqp://app-{e}:{passwords[f'app-{e}']}@rabbitmq.infra:5672/{e}",
            "ConnectionStrings__valkey": f"valkey.infra:6379,defaultDatabase={valkey_db[e]}"})
PY

while IFS= read -r plain; do
  target="${plain#"$work"/}"
  mkdir -p "$(dirname "$target")"
  sops --encrypt --filename-override "$target" --input-type yaml --output-type yaml "$plain" > "$target.tmp"
  mv "$target.tmp" "$target"
  echo "wrote $target"
done < <(find "$work" -name '*.sops.yaml' | sort)

echo "new-secrets: encrypted to $public_key. Commit .sops.yaml and the *.sops.yaml files."
if [ "$rotate" -eq 1 ]; then
  echo "On a running cluster, after Argo syncs: RabbitMQ and Keycloak read these only at startup, so restart them:"
  echo "  kubectl -n infra rollout restart statefulset/rabbitmq deployment/keycloak"
fi
