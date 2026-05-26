---
status: done
---

# Plan: `eru source add` command

## Context

Currently, registering a GitHub repo as a knowledge source requires manually editing `eru.json` or `~/.config/eru/config.json`. There is no CLI command for it. This creates friction for a core workflow: discover a useful repo, point eru at it, start pulling files.

The feature adds `eru source add <url>` which writes a `SourceConfig` entry to the appropriate config file and auto-detects the `KNOWLEDGE/` folder convention to set the source base path.

---

## Command shape

```
eru source add <url> [--name <name>] [--branch <branch>] [--basepath <path>] [--global]
```

- `<url>` — required positional: the git remote URL
- `--name` / `-n` — optional override; default is derived from URL (last path segment, `.git` stripped)
- `--branch` / `-b` — optional; stored as `Branch` on the `SourceConfig`
- `--basepath` / `-p` — optional; explicitly sets `SourceConfig.BasePath`, skipping auto-detection
- `--global` / `-g` — write to `~/.config/eru/config.json` instead of `eru.json`

**Name derivation examples:**
- `https://github.com/acme/knowledge-base.git` → `knowledge-base`
- `git@github.com:acme/knowledge-base.git` → `knowledge-base`

---

## Knowledge convention detection

If `--basepath` is provided it is used as-is and detection is skipped entirely.

Otherwise, eru calls `deps.ListRemoteTopLevel url branch` to list the repo's root-level entries. If `KNOWLEDGE` or `knowledge` appears, `SourceConfig.BasePath` is set to that value automatically. A message is printed to confirm: `Detected KNOWLEDGE/ convention — basePath set to "KNOWLEDGE"`.

If the adapter returns an error or empty list (including while git fetch is still stubbed), eru silently proceeds with `BasePath = None` and notes the basePath can be set manually.

---

## Error cases

| Situation | Behaviour |
|---|---|
| Local mode, `eru.json` missing | `Error: no eru.json found. Run 'eru init' first.` |
| Source name already exists in target config | `Error: source 'name' already exists.` |

---

## Files to create or modify

### New: `src/Eru.Domain/Source.fs`

```fsharp
module Source =
    type AddCommand = {
        Url      : string
        Name     : string option
        Branch   : string option
        BasePath : string option
        IsGlobal : bool
    }

    let private deriveNameFromUrl (url: string) : string  // strips .git, takes last segment
    let private detectBasePath (topLevel: string list) : string option  // checks KNOWLEDGE / knowledge
    let add (deps: Deps) (cmd: AddCommand) : int
```

Logic in `add`:
1. Derive name from `cmd.Name` or URL.
2. Resolve basePath: if `cmd.BasePath` is `Some`, use it directly (skip remote listing). Otherwise call `deps.ListRemoteTopLevel` and run detection.
3. Branch on `IsGlobal`:
   - **Local**: read `LocalConfig`; if `None` → error. Check for duplicate name. Build `SourceConfig`. Append and write via `deps.WriteLocalConfig`.
   - **Global**: read `GlobalConfig` (create empty if `None`). Check for duplicate. Build `SourceConfig`. Append and write via `deps.WriteGlobalConfig`.
4. Print confirmation (including basePath if any). Return `0`.

Add `Source.fs` to `Eru.Domain.fsproj` after `Deps.fs`.

### Modified: `src/Eru.Domain/Deps.fs`

Add three new fields:

```fsharp
WriteLocalConfig   : LocalConfig  -> Result<unit, string>
WriteGlobalConfig  : GlobalConfig -> Result<unit, string>
ListRemoteTopLevel : string -> string option -> Result<string list, string>
// url -> branch -> top-level entry names
```

### Modified: `src/Eru.Cli/Args.fs`

```fsharp
type SourceAddArgs =
    | [<MainCommand; ExactlyOnce>] Url      of url: string
    | [<AltCommandLine("-n")>]     Name     of name: string
    | [<AltCommandLine("-b")>]     Branch   of branch: string
    | [<AltCommandLine("-p")>]     Basepath of path: string
    | [<AltCommandLine("-g")>]     Global

[<CliPrefix(CliPrefix.None)>]
type SourceArgs =
    | [<SubCommand>] Add of ParseResults<SourceAddArgs>

// Add to EruArgs:
| [<SubCommand>] Source of ParseResults<SourceArgs>
```

### Modified: `src/Eru.Cli/Program.fs`

Add `SourceCmd` active pattern and dispatch case:
```fsharp
| SourceCmd cmd -> Source.add deps cmd
```

The active pattern maps `SourceArgs.Add` results to `Source.AddCommand`.

### Modified: `src/Eru.Adapters/ConfigAdapter.fs`

Add:
- `writeLocalConfig (cwd: string) (cfg: LocalConfig) : Result<unit, string>` — serializes via `Serialization.serialize` and calls `File.WriteAllText` at `Paths.localConfigPath cwd`
- `writeGlobalConfig (cfg: GlobalConfig) : Result<unit, string>` — same but at `Paths.globalConfigPath()`

Reuses existing `Serialization.serialize<'T>` (same STJ options as read path).

### Modified: `src/Eru.Adapters/AdapterDeps.fs`

Wire the three new `Deps` fields:
- `WriteLocalConfig` → `ConfigAdapter.writeLocalConfig cwd`
- `WriteGlobalConfig` → `ConfigAdapter.writeGlobalConfig`
- `ListRemoteTopLevel` → stub returning `Ok []` (same pattern as `FetchRemoteContent`)

### New: `tests/Eru.Tests/SourceTests.fs`

Test `Source.add` with in-memory deps (same pattern as `ConfigTests.fs`):

- Derives name from URL correctly (HTTPS and SSH forms)
- Writes to local config; errors if `eru.json` absent
- Writes to global config with `--global`
- Detects `KNOWLEDGE/` basePath from top-level listing
- Detects `knowledge/` basePath (lowercase)
- No basePath set when listing returns empty
- `--basepath` explicit value overrides auto-detection (remote listing not called)
- Duplicate name → error
- `--name` override respected

Add `SourceTests.fs` to `Eru.Tests.fsproj`.

---

## Verification

```bash
dotnet build
dotnet test

# Smoke tests:
eru init
eru source add https://github.com/acme/knowledge-base.git
cat eru.json   # source entry with auto-derived name "knowledge-base", no basePath (stub adapter)

eru source add https://github.com/acme/kb.git --name my-kb --branch stable --basepath docs
cat eru.json   # second entry with explicit name, branch, and basePath "docs"

eru source add https://github.com/acme/kb.git   # duplicate → error

eru source add https://github.com/acme/global-kb.git --global
cat ~/.config/eru/config.json   # global entry present
```
