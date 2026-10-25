# Coding standards for AI agents

These rules apply to every change made in this repo by an AI agent.

## Follow `.editorconfig`

- Write code that conforms to `.editorconfig` (naming, formatting, analyzer severities).
- The build uses `AnalysisMode=All` and `TreatWarningsAsErrors=true` (see `Directory.Build.props`). Fix the cause of a warning; do not work around it.

## No suppressions without permission

Ask the user first, and wait for an answer, before adding any of the following:

- `#pragma warning disable`, `[SuppressMessage]`, `[UnconditionalSuppressMessage]`, `[ExcludeFromCodeCoverage]`, `#nullable disable`
- `<NoWarn>` / `<WarningsNotAsErrors>` / `<NuGetAuditSuppress>` or any change to `TreatWarningsAsErrors` / `AnalysisMode`
- New or loosened `dotnet_diagnostic.*.severity` entries (or similar) in `.editorconfig`, `.globalconfig`, or `GlobalSuppressions.cs`
- The null-forgiving operator (`!`) in `src/` to silence a nullable warning

When asking, name the rule or warning, where it fires, and why fixing it properly is not viable.

## No new dependencies without permission

Ask first before adding or upgrading to a new:

- NuGet package (`Directory.Packages.props`, `PackageReference`)
- .NET tool (`.config/dotnet-tools.json`) or SDK change (`global.json`)
- Container image, Helm chart, or other deploy-time dependency under `deploy/` or `infra/`

Prefer the BCL and packages already referenced. State what the dependency is for and what the alternative is.

## Clean up when finishing code changes

After editing any `.cs` file (or project/props file), before reporting the work complete, follow the steps in `.claude/skills/cleanup/SKILL.md` (format with `--severity info`, verify, build, unit tests). Any agent can open and follow that file; Claude Code also exposes it as `/cleanup`. Do not include unrelated formatting churn in the change.

## Conventions this repo enforces

- Private fields, including `const` and `static readonly`, are `_camelCase` (IDE1006 via `.editorconfig`), e.g. `_maxPageSize`.
- Test namespaces match their folder: `Tests.Core`, `Tests.Worker`, etc. (IDE0130). Inside them `Core.` resolves to `Tests.Core`, so use a `using Core.Features...;` directive instead of writing `Core.Features...` inline.
- Source files are UTF-8 with a byte order mark (`charset = utf-8-bom` in `.editorconfig`); `dotnet format` adds it. Don't strip it.
- Use collection expressions (`[.. items]`) rather than `ToList()` / `ToArray()` where IDE0305 asks.
- CI runs `dotnet format opa.slnx --severity info --verify-no-changes`, which must pass. Unfixable diagnostics are fixed by hand; do not downgrade a rule to get past it without asking.
- Current approved `.editorconfig` opt-outs are listed there with a reason beside each: only extend that list with the user's permission.

## Write tests for new work

- New behavior and bug fixes come with tests in `tests/`, placed alongside the existing tests for the area (`Api`, `Core`, `Worker`, `Cedar`, `E2E`).
- Write the test first where practical, and run the relevant tests before reporting done.
- If a change genuinely can't be tested, say so and why in the summary rather than skipping silently.

## Build and test

```sh
dotnet build
dotnet test --project tests/Tests --filter-not-namespace "Tests.E2E"   # unit tests, no Docker needed
dotnet test --project tests/Tests --filter-namespace "Tests.E2E"       # E2E, needs Docker (Aspire starts the stack)
```

- Run the unit tests by default. Run `Tests.E2E` only when the change touches cross-service behavior, and expect it to be slow.
- If E2E fails because Docker isn't running or can't pull images, report that. Don't treat it as a code bug.
- Policy tests under `policies/` run with OPA (`docker run ... openpolicyagent/opa:latest test /policies`); see `README.md`.

## Failing checks

- When a build, test or format check fails, fix the cause.
- Never delete, skip or weaken a test or assertion to get green. Ask first, the same as for suppressions.

## Scope

- Keep changes to what was asked. No drive-by refactors, renames or unrelated formatting in the diff.
- If you notice something else worth fixing, mention it in the summary instead of changing it.

## Design preferences

- A seam with a single operation is a named delegate, not a one-method interface.

## Secrets

- Never commit secrets, tokens, passwords or decrypted files.
- Secrets live in `deploy/**/secrets/*.sops.yaml`, encrypted with SOPS (see `.sops.yaml`). Don't write plaintext values there, don't run `sops -d` to a file, and don't try to read or create the age key (`~/.config/sops/age/keys.txt`).
- Ask the user to create or rotate secrets (`deploy/bootstrap/new-secrets.sh`).

## Deploy

- Argo CD tracks `main`, so anything under `deploy/` goes live when merged. Ask before changing it, and read `deploy/README.md` first.

## Git

- Don't commit or push unless asked.
- Never add AI attribution to commits or PRs: no `Co-Authored-By` lines and no "Generated with" footers.
