# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working style

Do not write or scaffold any code unless the user explicitly uses the word **implement**. Discuss, plan, and update documentation freely — but make no code changes without that signal.

## Plan mode

When in plan mode, use `AskUserQuestion` to check for further refinements between iterations. Only call `ExitPlanMode` when the user explicitly says the plan is ready or asks to proceed.

## Project

`eru` is an F# dotnet 10 CLI tool for knowledge sharing between projects. It fetches files from configured knowledge sources (remote repos) and tracks what has been pulled into the local repo via a state file. Knowledge can be synced to and from a knowledge base.

## Toolchain

- **Runtime**: .NET 10 (managed via `mise` — run `mise install` to get the right version)
- **Language**: F# throughout
- **CLI parsing**: [Argu](https://fsprojects.github.io/Argu/)
- **Test framework**: xUnit v3
- **Shell commands**: [SimpleExec](https://github.com/adamralph/simple-exec)

## Commands

```bash
# Build
dotnet build

# Run all tests
dotnet test

# Run a single test (by name filter)
dotnet test --filter "FullyQualifiedName~TestName"

# Run the tool locally
dotnet run --project src/Eru -- <args>

# Pack as a global tool
dotnet pack src/Eru
dotnet tool install --global --add-source ./src/Eru/nupkg eru
```

## Architecture

The tool is structured around three core concepts:

1. **Knowledge sources** — configured remote repositories (or paths) that serve as the canonical source of truth for shared files. Sources have a priority/preference order for search.

2. **State file** — a file committed in the consuming repo (e.g. `eru.lock` or similar) that records every piece of knowledge pulled in: source, version/ref, and local path. This enables sync in both directions.

3. **CLI commands** (via Argu):
   - `search` — search across configured knowledge sources
   - `add` — pull a specific file/snippet into the repo ad-hoc and record it in the state file
   - `sync` — reconcile the state file against knowledge sources (pull updates or push local changes back)
   - `init` — scaffold a configuration file for a new repo

### Data flow

```
Config file (eru.json)
    │
    ▼
Knowledge sources (remote repos, local paths)
    │
    ▼
State file (tracks what is in this repo + where it came from)
    │
    ▼
Local repo files
```

The state file is the source of truth for what knowledge lives in a given repo. Config defines where to look; the state file defines what was fetched.

## Key conventions

- All CLI argument types are defined as Argu `IArgParserTemplate` discriminated unions.
- Side-effectful operations (git, filesystem) are isolated from pure domain logic.
- SimpleExec is used for shelling out to `git` (cloning, fetching, reading blobs).
- Configuration is read from a JSON file in the repo root (using standard `System.Text.Json` — no third-party JSON libs).
