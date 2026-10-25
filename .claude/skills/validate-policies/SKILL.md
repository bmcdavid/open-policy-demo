---
name: validate-policies
description: Validate and test Rego and Cedar policy changes in policies/. Use after editing authz.rego, authz_test.rego or authz.cedar, before reporting complete or deploying.
---

# Validate policies

Run from the repo root. Stop and fix the cause if a step fails; never weaken or delete a test to get green.

Both engines must give the same answers and deny reasons: dev and prod use OPA, staging uses Cedar. A change to `policies/authz.rego` almost always needs the matching change in `policies/authz.cedar`, and the reverse.

No `opa` or `cedar` binary is installed. Use Docker for OPA and the repo's own tool for Cedar. If Docker is not running, report that; it is not a code bug.

1. Test first. Add or change a case in `policies/authz_test.rego` before changing the rule.

2. Format and check the Rego. `fmt --write` rewrites files in place; to only see what would change, use `fmt --list --fail` on a read-only mount (it lists the files and exits non-zero). Plain `fmt --fail` prints the whole formatted source, so don't:

   ```sh
   docker run --rm -v "$PWD/policies:/policies" openpolicyagent/opa:1.21.4 fmt --write /policies
   docker run --rm -v "$PWD/policies:/policies:ro" openpolicyagent/opa:1.21.4 check --strict /policies
   ```

3. Run the Rego tests (same version `deploy/build/build-policies.sh` pins):

   ```sh
   docker run --rm -v "$PWD/policies:/policies:ro" openpolicyagent/opa:1.21.4 test /policies --verbose
   ```

4. Validate the Cedar file. Every rule needs a unique `@id("...")`; the ids of `forbid` rules are the user-visible deny reasons. Errors carry line numbers:

   ```sh
   out=$(mktemp -d) && dotnet run --project src/CedarPolicyTool -- generate policies/authz.cedar "$out/policies.json"
   ```

   Do not edit `infra/cedar/policies.json`; it is generated and git-ignored.

5. Run the .NET tests that check the policies and the engines agree:

   ```sh
   dotnet test --project tests/Tests --filter-namespace "Tests.Cedar"
   ```

   `Tests.E2E` (`ScenarioMatrixTests`) compares every user, tenant, environment and action, but needs Docker and is slow. Run it when a rule's behavior changed.

6. If the change is going to the cluster, continue with the `deploy-lab` skill. `build-policies.sh` reruns steps 3 and 4 before it pushes anything.

Report which steps ran and their results. If you changed only `.rego`/`.cedar` files, the `cleanup` skill is not needed.
