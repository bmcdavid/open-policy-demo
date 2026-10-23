# Learning Kubernetes with this lab

The lab ([README](README.md)) is a "day 30" setup: Argo CD applies everything from Git, so you never see the basic
loop of writing YAML, applying it and watching what happens. Learn that loop first on a throwaway cluster, then read
the lab's files in the order below.

## 1. The core objects

Seven objects cover most of the lab. The ECS column is a rough equivalent, not an exact match.

| Kubernetes | ECS | In this lab |
|---|---|---|
| **Pod**: one or more containers scheduled together | running task | api pod = `api` + OPA + Cedar sidecars |
| **Deployment**: keeps N pods running, rolls out new versions | service | [`apps/api/base/api-deployment.yaml`](apps/api/base/api-deployment.yaml) |
| **Service**: stable internal name and IP in front of pods | service discovery / target group | [`apps/api/base/service.yaml`](apps/api/base/service.yaml) |
| **Namespace**: a folder for objects | cluster or account split | `app-dev`, `infra`, ... in [`platform/namespaces/namespaces.yaml`](platform/namespaces/namespaces.yaml) |
| **ConfigMap / Secret**: config and passwords handed to pods | task definition env vars / Secrets Manager | [`infra/secrets/`](infra/secrets/) |
| **Gateway + HTTPRoute** (or Ingress): outside traffic in | ALB + listener rules | [`apps/api/base/httproute.yaml`](apps/api/base/httproute.yaml) |
| **StatefulSet**: pods with stable names and storage | – | [`infra/rabbitmq/statefulset.yaml`](infra/rabbitmq/statefulset.yaml) |

## 2. Hands-on on a throwaway cluster

Use a separate cluster named `play` so nothing touches the lab. Both can run at once.

```bash
kind create cluster --name play          # also switches kubectl's current context to kind-play
kubectl config current-context           # must print kind-play before you go on

kubectl create deployment web --image=nginx --replicas=2
kubectl get pods -o wide
kubectl delete pod <one-of-them>         # a new one appears: the Deployment keeps 2 running
kubectl expose deployment web --port=80  # a Service in front of the pods
kubectl scale deployment web --replicas=4
kubectl get deployment web -o yaml       # the YAML behind what you just did
```

Reach it from the laptop:

```bash
kubectl port-forward svc/web 8080:80     # open http://localhost:8080
```

`port-forward` runs in the foreground and holds the terminal until you press `Ctrl+C`. Run it in a second terminal
tab, or in the background with `kubectl port-forward svc/web 8080:80 &` and stop it later with `kill %1`.

Then do it the way the lab does: write the Deployment and Service as YAML files and `kubectl apply -f` them. Change
the file (replicas, image tag), apply again, and watch `kubectl get pods -w`. That loop is the core skill; Argo CD only
automates it.

The official [Kubernetes Basics tutorial](https://kubernetes.io/docs/tutorials/kubernetes-basics/) covers the same
ground in more depth.

### Clean up

```bash
kill %1 2>/dev/null                      # a background port-forward, if any
kind delete cluster --name play          # removes everything above; "lab" is a different name
```

Plain `kubectl` goes to the current context. If you skipped `kind create cluster --name play`, `web` may have landed
in the lab: check with `kubectl --context kind-lab get deploy,svc -A | grep web` and delete just those two objects.

## 3. Read the lab's plain YAML

1. [`infra/valkey/`](infra/valkey/): one Deployment and one Service, almost nothing else. The simplest piece.
2. [`apps/api/base/api-deployment.yaml`](apps/api/base/api-deployment.yaml): the same idea plus native sidecars
   (OPA, Cedar), probes, and resource requests and limits.
3. [`infra/keycloak/httproute.yaml`](infra/keycloak/httproute.yaml): how `keycloak.localtest.me` reaches a Service.

## 4. Look at the running lab (read-only)

```bash
kubectl --context kind-lab get pods -A
kubectl --context kind-lab -n app-dev describe pod <api-pod>     # Events at the end explain most problems
kubectl --context kind-lab -n app-dev logs <api-pod> -c api      # -c picks the container: api, opa, cedar
kubectl --context kind-lab -n app-dev get deploy,svc,configmap,secret
```

Edits made by hand in `app-dev` and `app-staging` are reverted by Argo within seconds (self-heal). That is GitOps
working, not a bug; see [README section 4](README.md#4-environments).

## 5. Add the layers one at a time

| Layer | What to learn | Try |
|---|---|---|
| Kustomize | why there is a `base/` and `overlays/{dev,staging,prod}` | from the repo root, `kubectl kustomize deploy/apps/api/base` prints the shared YAML; an overlay needs the KSOPS plugin: `kustomize build --enable-alpha-plugins --enable-exec deploy/apps/api/overlays/dev` (prints decrypted secrets, so never redirect it into the repo) |
| Argo CD | it runs that render-and-apply step from Git | open the UI ([README section 3](README.md#3-use-it)), compare an app's live and desired state |
| Gateway API / Envoy | how `*.localtest.me` reaches a Service | [`platform/gateway/`](platform/gateway/), then the HTTPRoutes |
| Gatekeeper | admission rules that reject bad manifests | [README section 6](README.md#6-guardrails-gatekeeper) |
| SOPS / KSOPS | encrypted secrets in Git | [README section 5](README.md#secrets) |

Gatekeeper and SOPS matter for production clusters but are not needed to understand Kubernetes itself.

## 6. Stopping clusters

Stop a kind cluster with `kind delete cluster --name <name>`, never by stopping its containers in Docker Desktop or
with `docker stop`: that froze Docker Desktop's VM once while this lab was built. Rebuilding the lab takes about 5
minutes ([README section 7](README.md#7-stop-rebuild-switch-to-aspire)).
