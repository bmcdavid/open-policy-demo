---
name: deploy-lab
description: Build, pin, promote and debug the local kind + Argo CD Kubernetes lab. Use when changing anything under deploy/, shipping app or policy code to dev/staging/prod, or diagnosing the cluster.
---

# Deploy lab

The lab is a kind cluster `lab` run by Argo CD from Git. `deploy/README.md` is the source of truth; read the section you need rather than relying on this summary. Section numbers below refer to it.

## Rules

- Argo tracks `main`, so a push of `deploy/` changes goes live. Ask before committing or pushing, and before syncing `api-prod`.
- Pass `--context kind-lab` to every `kubectl` and `argocd` command. Docker Desktop restarts switch the current context to `docker-desktop`.
- Never run Aspire (`dotnet run --project src/AppHost`) while the cluster is up, and never `docker stop` the nodes. To switch, `kind delete cluster --name lab` and rebuild (section 7).
- Run `deploy/build/validate.sh` before every push of a `deploy/` change. If you wrap it, keep its `</dev/null` (gator waits on stdin).
- Build scripts tag with the git short SHA and refuse uncommitted inputs. Commit first. `ALLOW_DIRTY=1` is for throwaway builds only.
- Secrets: never run `sops -d` to a file, never read or create `~/.config/sops/age/keys.txt`, never commit a decrypted file. Ask the user to run `deploy/bootstrap/new-secrets.sh`.
- Gatekeeper rules fail the sync, not the push: no `:latest`, images only from `localhost:5001/`, resource limits and the `app.kubernetes.io/name` and `owner` labels on every container, 2+ api replicas in prod.

## Pre-flight

```sh
docker info >/dev/null && kind get clusters && kubectl --context kind-lab get nodes
deploy/bootstrap/check-tools.sh
```

If Docker is down or there is no cluster, report that and stop; rebuild only if asked (`deploy/bootstrap/cluster.sh && deploy/bootstrap/argocd-install.sh`, about 5 minutes, then sync `api-prod` by hand).

## Ship a change to dev (section 5)

Policy changes: validate first with the `validate-policies` skill, and change Rego and Cedar together.

```sh
git commit -am "..."                                                               # the build tags HEAD
deploy/build/pin.sh dev --images "$(deploy/build/build-images.sh | tail -1)"       # src/ changes
deploy/build/pin.sh dev --policies "$(deploy/build/build-policies.sh | tail -1)"   # policies/ changes
deploy/build/validate.sh                                                           # then commit the pin and push
```

After changing the api base or the dev overlay, run `deploy/build/validate.sh --refresh-fixture`.

## Promote

`deploy/build/promote.sh dev staging`, review the tag-only diff, commit, push (staging rolls automatically). Then `deploy/build/promote.sh staging prod`, commit, push. `api-prod` shows OutOfSync until someone syncs it with **Prune** ticked, in the Argo UI or with the patch in section 2.5. Nothing is rebuilt on promotion.

## Check

```sh
kubectl --context kind-lab -n argocd get applications          # 12, Synced/Healthy; api-prod may be OutOfSync
kubectl --context kind-lab -n <ns> get pods
kubectl --context kind-lab -n <ns> describe pod <pod>          # Events at the end
kubectl --context kind-lab -n <ns> logs <pod> -c <container>
kubectl --context kind-lab get constraints                     # Gatekeeper audit counts
curl -s http://api.dev.localtest.me/buildinfo                  # GitHash is the running tag
```

E2E against dev is in section 3 (`E2E_TARGET=k8s ...`). A request with no running cluster is an environment problem, not a code bug.

## Troubleshoot

Match the symptom in section 8 first. The common ones: wrong kubectl context; `ImagePullBackOff` (tag never built, so build and pin it); `ComparisonError` mentioning ksops (re-run `argocd-install.sh`); `denied by validation.gatekeeper.sh` (the message names the rule, and `validate.sh` shows it locally); prod OutOfSync "requires pruning" (sync again with Prune); Docker VM stalled (restart Docker Desktop).
