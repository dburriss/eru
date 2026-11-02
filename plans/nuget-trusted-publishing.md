# Plan: NuGet publish via Trusted Publishing

## Context
`.github/workflows/publish-nuget.yml` pushes to nuget.org with a long-lived API key (`secrets.NUGET_DEPLOY_KEY`). Switch to NuGet Trusted Publishing (GitHub OIDC -> short-lived key via `NuGet/login@v1`), so there is no long-lived secret to leak or rotate. Per AGENTS.md, nothing is changed until the user says "implement".

## Changes

### 1. `.github/workflows/publish-nuget.yml`
- Add job-level `permissions: id-token: write` on `publish` (top-level `contents: read` stays).
- Replace the `NUGET_AUTH_TOKEN` env on the push step with a login step before it:
  ```yaml
  - name: NuGet login (OIDC to temp API key)
    uses: NuGet/login@v1
    id: login
    with:
      user: ${{ secrets.NUGET_USER }}   # nuget.org profile name, not email
  ```
- Push step uses `--api-key ${{ steps.login.outputs.NUGET_API_KEY }}` (keep `--skip-duplicate`).
- Keep the file name `publish-nuget.yml` (the policy is bound to it) and keep the existing .NET setup/build/test/pack steps as they are.
- Optional (not required for this task): the skill's template also bumps checkout/setup-dotnet to v5 and packs with `-p:Version` from the tag. I'd leave these out to keep the change focused.

### 2. `AGENTS.md`
Update the `publish-nuget.yml` bullet under "CI and releases": replace "using the `NUGET_DEPLOY_KEY` secret" with "using NuGet Trusted Publishing (`NuGet/login@v1`, `NUGET_USER` secret holding the nuget.org username)".

### 3. `CHANGELOG.md`
No entry needed (CI-only, not user-facing). Commit as `ci: Publish to NuGet with Trusted Publishing` (Conventional Commits, no Co-Authored-By per AGENTS.md).

## Manual steps for the user (Claude cannot do these)
1. Find the nuget.org username (profile name under `nuget.org/profiles/<name>`, not the email).
2. nuget.org -> Trusted Publishing -> Add policy: repository owner `dburriss`, repository `eru`, workflow file `publish-nuget.yml`, environment empty, package `Eru.Tool`.
3. GitHub repo -> Settings -> Secrets -> Actions: add `NUGET_USER`.
4. After the first successful OIDC publish, delete the `NUGET_DEPLOY_KEY` secret and revoke the key on nuget.org.

(Owner/repo should be confirmed from `git remote get-url origin`.)

## Verification
- The workflow can't be run locally; validate YAML syntax and grep that no `NUGET_DEPLOY_KEY` references remain.
- Real test: push a prerelease tag (e.g. `v0.14.1-alpha.1`) after steps 1-3. A failing "NuGet login" step means the policy or `NUGET_USER` is wrong; a 403 on push means the policy doesn't cover the package id.
