---
name: cleanup
description: Format and verify .NET code before finishing a change. Use when code changes are done, before reporting complete or committing.
---

# Cleanup

Run from the repo root, in order. Stop and fix the cause if a step fails.

1. Format, including `suggestion`-level rules (`--severity info`, since the default is `warn`):

   ```sh
   dotnet format opa.slnx --severity info
   ```

2. Verify nothing is left unformatted, as CI does (non-zero exit means more work):

   ```sh
   dotnet format opa.slnx --severity info --verify-no-changes
   ```

   If it still reports diagnostics, `dotnet format` cannot fix them: fix them by hand. Do not set rules to `none`
   or add suppressions without asking (see `AGENTS.md`).

3. Build, then run the unit tests (no Docker needed):

   ```sh
   dotnet build
   dotnet test --project tests/Tests --filter-not-namespace "Tests.E2E"
   ```

4. Review `git diff --stat`. If files you didn't touch were reformatted, tell the user instead of keeping or reverting them silently.

Never add suppressions, skip tests or loosen `.editorconfig` to get green. Ask first (see `AGENTS.md`).
