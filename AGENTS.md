# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working style

Do not write or scaffold any code unless the user explicitly uses the word **implement**. Discuss, plan, and update documentation freely — but make no code changes without that signal.

## Project

`eru` is an F# dotnet 10 CLI tool for knowledge sharing between projects. It fetches files from configured knowledge sources (remote repos) and tracks what has been pulled into the local repo via a state file. Knowledge can be synced to and from a knowledge base.

## Toolchain

- **Runtime**: .NET 10 (managed via `mise` — run `mise install` to get the right version)
- **Language**: F# throughout
- **CLI parsing**: [Argu](https://fsprojects.github.io/Argu/)
- **Test framework**: xUnit v3
- **Shell commands**: [SimpleExec](https://github.com/adamralph/simple-exec)
- **Console UI**: [Spectre.Console](https://spectreconsole.net/) for tables, spinners, and prompts; [Terminal.Gui](https://gui-cs.github.io/Terminal.Gui/) for interactive browsing (`eru browse`)
## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test --solution eru.slnx

# Run a single test (by name filter)
dotnet test --solution eru.slnx -- --filter-method "*TestName*"

# Run the tool locally
dotnet run --project src/Eru.Cli -- <args>   # or: mise run eru -- <args>

# Pack as a global tool
dotnet pack src/Eru.Cli
dotnet tool install --global --add-source ./src/Eru.Cli/nupkg Eru.Tool

# Install a patch-bumped alpha build of the current checkout (mise task, wraps scripts/install-alpha.fsx)
mise run install-alpha            # add --dry-run via ./install-alpha.sh --dry-run

# Start the MCP server from source
mise run mcp

# Cut a release (interactive; bumps version, rolls CHANGELOG, tags, optionally pushes)
dotnet fsi scripts/publish.fsx --fsproj src/Eru.Cli/Eru.Cli.fsproj --solution eru.slnx
```

## Project layout

`src/` holds `Eru.Domain` (pure logic), `Eru.Adapters` (git/filesystem), `Eru.Cli` (Argu entry point, packed as the `Eru.Tool` NuGet package with command `eru`), `Eru.Mcp`, `Eru.Search`, `Eru.Site`, `Eru.Serve` and `Eru.Tui` (Terminal.Gui browser behind `eru browse`). Design notes live in `plans/`, user docs in `docs/` (Diataxis), agent skills in `skills/`, and the built-in ingestor template in `agents/`.

## CI and releases

- `.github/workflows/build.yml` — restore, build and test (Release) on Ubuntu for every push and PR. It does not run on Windows or macOS, so OS-specific bugs (e.g. path separators) only surface in the publish workflows.
- `publish-gh.yml` — on `v*` tags (or manual dispatch): builds and tests on Linux, Windows and macOS, publishes trimmed single-file self-contained binaries (`linux-x64`, `win-x64`, `osx-x64`) and attaches them to a GitHub Release. Release notes come from the matching `## [x.y.z]` section of `CHANGELOG.md`; tags containing `-` are marked prerelease.
- `publish-nuget.yml` — on `v*` tags (or manual dispatch): builds, tests, packs `src/Eru.Cli` and pushes to nuget.org using the `NUGET_DEPLOY_KEY` secret.
- Keep `CHANGELOG.md` `## [Unreleased]` up to date; the publish script and release notes depend on it.

## Architecture

The tool is structured around three core concepts:

1. **Knowledge sources** — configured remote repositories (or paths) that serve as the canonical source of truth for shared files. Sources have a priority/preference order for search.

2. **State file** — `.eru/eru.lock`, committed in the consuming repo, records every piece of knowledge pulled in: source, version/ref, and local path. This enables sync in both directions.

3. **CLI commands** (via Argu):
   - `search` — search across configured knowledge sources
   - `add` — pull a specific file/snippet into the repo ad-hoc and record it in the state file
   - `sync` — reconcile the state file against knowledge sources (pull updates or push local changes back)
   - `init` — scaffold a configuration file for a new repo
   - plus `source`, `collection`, `manifest`, `inbox`, `site`, `okf`, `cache`, `browse`, `remove`, `disconnect`, `mcp` and `version` — see `docs/reference/cli.md` for the full list

### Data flow

```
.eru/config.json  (local consumer config)
    │
    ▼
Knowledge sources (remote repos, local paths)
  — each may expose .eru/manifest.json to declare available artifacts
    │
    ▼
.eru/eru.lock  (tracks what is in this repo + where it came from)
    │
    ▼
Local repo files
```

The lock file is the source of truth for what knowledge lives in a given repo. Config defines where to look; the lock file defines what was fetched.

## Key conventions

- All CLI argument types are defined as Argu `IArgParserTemplate` discriminated unions.
- Side-effectful operations (git, filesystem) are isolated from pure domain logic.
- SimpleExec is used for shelling out to `git` (cloning, fetching, reading blobs).
- **Domain paths are always `/`-separated.** Remote paths, bundle-relative paths and anything compared against mocked `Deps` paths must never be built or split with `System.IO.Path.Combine`, `GetDirectoryName` or `GetFileName` inside `src/Eru.Domain` — on Windows those emit `\` and break tests and CI (the Windows publish job fails while Linux/macOS pass). Use `PathJoin.Combine`, `PathUtil.dirName` and `PathUtil.fileName` from `src/Eru.Domain/PathUtil.fs`. `System.IO.Path` is fine only in the adapters/CLI layer for real OS paths. Tests must use `/` literals, not `Path.Combine`.
- Configuration is read from `.eru/config.json` in the repo's `.eru/` directory (using standard `System.Text.Json` — no third-party JSON libs).
