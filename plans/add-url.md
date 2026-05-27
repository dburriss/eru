---
status: done
---
# Plan: `eru add` URL shorthand

## Context

`eru add` currently requires a source to be pre-configured before you can pull a file. The two-step workflow (`eru source add <repo-url>` then `eru add <source>:<path>`) creates friction when a user has a direct GitHub link to a file they want to pull. The goal is to let users paste a provider URL directly — `eru add https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md` — and have the tool auto-register the source (locally by default, globally with `--global`) and pull the file in one step.

## Behaviour

- If `remote_path` starts with `https://` it is treated as a provider URL, not a bare path.
- The URL is parsed to extract: repo URL, branch, remote file path, and a derived source name.
- If a source with that name **already exists** in the effective config it is reused silently.
- If a source with that name exists but points to a **different URL** the command fails with a clear error.
- If no source with that name exists it is added to **local config** (`eru.json`) by default, or to **global config** (`~/.config/eru/config.json`) when `--global` is passed.
- After the source is resolved the normal `pullOne` flow runs unchanged.
- `eru.json` must already exist for the local path (same guard as `Source.add`); if absent the error message directs the user to `eru init`.

## Supported URL formats

Start with GitHub only. Fail with a clear "unsupported provider" error for unrecognised HTTPS URLs.

```
https://github.com/{owner}/{repo}/blob/{branch}/{path...}
```

## Files to create / modify

### 1. New: `src/Eru.Domain/UrlParser.fs`

Pure module — no `Deps`, no I/O.

```fsharp
type ParsedProviderUrl = {
    RepoUrl    : string   // https://github.com/owner/repo
    Branch     : string
    RemotePath : string   // knowledge/github-cli.md
    SourceName : string   // derived same way as Source.deriveNameFromUrl
}

// Returns None if the string is not a recognised provider URL.
val tryParse : string -> ParsedProviderUrl option
```

GitHub detection: `Uri` host = `github.com`, segments match `/blob/<branch>/`.

Add `UrlParser.fs` to `src/Eru.Domain/Eru.Domain.fsproj` **before** `Add.fs`.

### 2. `src/Eru.Domain/Add.fs`

**`Add.Command`** — add one field:
```fsharp
IsGlobal : bool
```

**`Add.run`** — in the single-file branch (the `| _` match after collection/tag checks), before `parseDiscriminator`, detect a URL:

```fsharp
match UrlParser.tryParse rawPath with
| Some parsed ->
    // 1. Check effective sources for name clash or reuse
    // 2. If not found: write new SourceConfig to local (or global) config
    //    Call deps.ListRemoteTopLevel for basePath auto-detection (same as Source.add)
    // 3. Append new source to in-memory eff.Sources (avoid second config read)
    // 4. Fall through to pullOne with parsed.SourceName and parsed.RemotePath
| None ->
    // existing discriminator path unchanged
```

For source creation, add a private `ensureSource` helper in `Add.fs` that mirrors the duplicate-check + write logic from `Source.add` (`Source.fs:43-83`).

### 3. `src/Eru.Cli/Args.fs`

Add `Global` flag to `AddArgs`:
```fsharp
| [<Unique>] Global
```
Usage: `"Write auto-created source to global config (~/.config/eru/config.json)."`

### 4. `src/Eru.Cli/CommandMapper/CommandMapper.fs`

Update `(|AddCmd|_|)` to map `IsGlobal = addArgs.Contains AddArgs.Global`.

### 5. `tests/Eru.Tests/UrlParserTests.fs`

Pure-function tests (no `Deps`) following the `ConfigTests.fs` pattern.

| Test | Expected |
|---|---|
| GitHub blob URL → parsed correctly | `RepoUrl`, `Branch`, `RemotePath`, `SourceName` all correct |
| Non-URL path → `None` | `tryParse "knowledge/foo.md" = None` |
| Non-GitHub HTTPS URL → `None` | `tryParse "https://gitlab.com/..." = None` |

Add file to `Eru.Tests.fsproj` before `AddTests.fs`.

### 6. `tests/Eru.Tests/AddTests.fs`

Add test cases using the existing `makeDeps` / `CapturedState` pattern:

| Scenario | Assertion |
|---|---|
| GitHub URL, source not in config → source written to local config, file pulled | `state.WrittenFiles` has entry; captured local config has new source |
| GitHub URL, source already in config → no write to config, file pulled | `WriteLocalConfig` not called; file still pulled |
| GitHub URL, source name exists with different URL → exit code 1 | `state.WrittenFiles` empty |
| GitHub URL + `--global`, source not in config → source written to global config | captured global config has new source |

## Reused utilities

- `Source.deriveNameFromUrl` (`Source.fs:12-15`) — duplicate the 3-line logic into `UrlParser.fs` to keep it dependency-free.
- `Source.detectBasePath` (`Source.fs:17-18`) — replicate in `Add.ensureSource`.
- `deps.ListRemoteTopLevel`, `deps.ReadLocalConfig`, `deps.WriteLocalConfig`, `deps.WriteGlobalConfig`, `deps.ReadGlobalConfig` — already wired up in `Add.run`.

## Verification

```bash
dotnet test

# integration smoke test
cd /tmp/test-repo && eru init
eru add https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md --dry-run
# → "Would pull knowledge/github-cli.md → knowledge/github-cli.md"
# → eru.json now contains orcai source

eru add https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md --dry-run
# → succeeds without "already exists" error

eru add https://github.com/dburriss/orcai/blob/main/knowledge/github-cli.md --global --dry-run
# → source written to ~/.config/eru/config.json
```
