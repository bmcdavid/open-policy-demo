#!/usr/bin/env bash
# Local image registry shared by the laptop and the kind nodes (kind's documented local-registry pattern).
# The laptop pushes to localhost:5001. Inside a node "localhost" is the node itself, so each node's containerd
# gets a mirror file mapping localhost:5001 to http://kind-registry:5000 on the shared "kind" Docker network.
# Idempotent: every step checks before it acts.
set -euo pipefail

reg_name=kind-registry
reg_port=5001
reg_image=registry:3.1.2
context=kind-lab

if [ "$(docker inspect -f '{{.State.Running}}' "$reg_name" 2>/dev/null || true)" != true ]; then
  if docker inspect "$reg_name" >/dev/null 2>&1; then
    docker start "$reg_name" >/dev/null
  else
    docker run -d --restart=always -p "127.0.0.1:${reg_port}:5000" --network bridge --name "$reg_name" "$reg_image" >/dev/null
  fi
  echo "registry: started $reg_name"
fi

if [ "$(docker inspect -f '{{json .NetworkSettings.Networks.kind}}' "$reg_name")" = null ]; then
  docker network connect kind "$reg_name"
  echo "registry: connected $reg_name to the kind network"
fi

hosts_dir="/etc/containerd/certs.d/localhost:${reg_port}"
hosts_toml="[host.\"http://${reg_name}:5000\"]"
for node in $(kind get nodes --name lab); do
  if [ "$(docker exec "$node" cat "$hosts_dir/hosts.toml" 2>/dev/null || true)" != "$hosts_toml" ]; then
    docker exec "$node" mkdir -p "$hosts_dir"
    printf '%s\n' "$hosts_toml" | docker exec -i "$node" cp /dev/stdin "$hosts_dir/hosts.toml"
    echo "registry: mirror configured on $node"
  fi
done

# Tells tools in the cluster where the local registry is (KEP-1755).
kubectl --context "$context" apply -f - <<EOF | grep -v ' unchanged$' || true
apiVersion: v1
kind: ConfigMap
metadata:
  name: local-registry-hosting
  namespace: kube-public
data:
  localRegistryHosting.v1: |
    host: "localhost:${reg_port}"
    help: "https://kind.sigs.k8s.io/docs/user/local-registry/"
EOF
