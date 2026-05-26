---
status: done
---

# Plan: eru Config and State File Design

## Context

`eru` is a new F# .NET 10 CLI tool (no source code exists yet). Before implementing any commands, the config and state file schemas need to be defined — they are the foundation everything else builds on. This plan establishes the three file formats and the F# types that represent them.

## Three Files

| File | Location | Committed? | Purpose |
|---|---|---|---|
| Global config | `~/.config/eru/config.json` (XDG; `%APPDATA%\eru\config.json` on Windows) | No | Default sources and user-level defaults |
| Local config | `eru.json` at repo root | Yes | Per-project sources + local overrides |
| State file | `eru.lock` at repo root | Yes | Audit trail of every pulled file |

## F# Record Types

### Shared (`src/Eru/Config.fs`)

A single `SourceConfig` type is used in both global and local config. The only semantic difference is that global sources must have a `Url` (validated at load time); local sources may omit it to inherit by `Name` from the global config.

```fsharp
type SourceConfig = {
    Name: string           // unique key; local entries may omit Url and inherit by this name
    Url: string option     // required in global config (validated); optional in local (inherits global)
    Branch: string option  // default branch; None = remote HEAD or falls back to GlobalDefaults
    BasePath: string option  // scope searches to a sub-path within the repo
}

// A single file entry within a collection. Source + path point at a file in a configured source.
// Tags here are additive: the file inherits the collection's tags plus its own.
type CollectionFileRef = {
    Source: string         // references a SourceConfig.Name from merged config
    RemotePath: string     // path within that source repo
    Tags: string list      // file-level tags (in addition to collection tags)
}

type CollectionConfig = {
    Name: string
    Tags: string list      // collection-level tags, e.g. ["backend"; "dotnet"]
    Files: CollectionFileRef list
}

type GlobalDefaults = {
    Branch: string option      // fallback branch for sources that don't specify one
    CommitOnPull: bool option   // None = false
}

type GlobalConfig = {
    Version: int
    DefaultSources: SourceConfig list
    Collections: CollectionConfig list
    Defaults: GlobalDefaults option
}

type LocalSettings = {
    CommitOnPull: bool option  // overrides GlobalDefaults.CommitOnPull
    StateFile: string option   // override "eru.lock" filename; almost never set
}

type LocalConfig = {
    Version: int
    Sources: SourceConfig list
    Settings: LocalSettings option
}
```

### State File (`src/Eru/LockFile.fs`)

The lock file is a plain line-based text file — one entry per line, tab-separated. No JSON. Branch is not stored here; it is resolved from the source config at sync time.

```fsharp
type LockEntry = {
    LocalPath: string    // relative to repo root, forward slashes, e.g. "docs/adr.md"
    SourceName: string   // references a SourceConfig.Name from merged config
    RemotePath: string   // path within the source repo, e.g. "shared/templates/adr.md"
    ContentHash: string  // "sha256:<lowercase-hex>" — detects local drift
}
```

Three columns on disk: `localPath\tsourceName:remotePath\tcontentHash`. `SourceName` and `RemotePath` are kept separate in the F# type (easier to work with in code) but serialized as a single combined column using `:` as separator — they describe one thing (the origin) and are never useful apart from each other. The parser splits the second column on the first `:`.

Fields dropped vs earlier design: `Ref`, `CommitSha`, `PulledAt`, `CollectionName`. These were audit-trail niceties; the lock file's only job is to know *where a file came from* and *whether it has drifted*. Branch resolution happens at runtime via source config.

## JSON Examples

**`~/.config/eru/config.json`:**
```json
{
  "version": 1,
  "defaultSources": [
    {
      "name": "company-templates",
      "url": "https://github.com/acme/knowledge-base.git",
      "branch": "main",
      "basePath": "shared"
    },
    {
      "name": "platform-docs",
      "url": "https://github.com/acme/platform.git",
      "branch": "main",
      "basePath": null
    }
  ],
  "collections": [
    {
      "name": "dotnet-backend-starter",
      "tags": ["backend", "dotnet"],
      "files": [
        { "source": "company-templates", "remotePath": "dotnet/logging/Logging.fs", "tags": ["dotnet"] },
        { "source": "company-templates", "remotePath": "dotnet/adr-template.md", "tags": ["docs"] },
        { "source": "platform-docs", "remotePath": "shared/deploy.sh", "tags": ["ops"] }
      ]
    },
    {
      "name": "frontend-starter",
      "tags": ["frontend"],
      "files": [
        { "source": "company-templates", "remotePath": "js/eslint.config.js", "tags": [] },
        { "source": "company-templates", "remotePath": "js/tsconfig.base.json", "tags": [] }
      ]
    }
  ],
  "defaults": {
    "branch": "main",
    "commitOnPull": false
  }
}
```

**`eru.json`:**
```json
{
  "version": 1,
  "sources": [
    { "name": "company-templates" },
    {
      "name": "team-patterns",
      "url": "https://github.com/acme/team.git",
      "branch": "stable"
    }
  ],
  "settings": null
}
```

Note: `{ "name": "company-templates" }` with no `url` means "inherit the global source by this name."

**`eru.lock`:**
```
# eru.lock v1
docs/adr-template.md    company-templates:dotnet/adr-template.md    sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
scripts/deploy.sh       platform-docs:shared/deploy.sh              sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08
src/Utils.fs            company-templates:dotnet/Utils.fs           sha256:abc123abc123abc123abc123abc123abc123abc123abc123abc123abc123abc1
```

Format: tab-separated `localPath\tsourceName:remotePath\tcontentHash` (3 columns). First line is a version comment (`# eru.lock v1`), parsed to detect schema migrations. Lines starting with `#` and blank lines are ignored. Sorted by `localPath` on write so diffs are stable.

## Source Merging Semantics (`src/Eru/Config.fs` — `mergeConfigs`)

1. Start with `GlobalConfig.DefaultSources` as the base list (in declared order).
2. For each entry in `LocalConfig.Sources`:
   - If `Url` is `Some`: fully-specified; replace any global source with the same `Name`, or append if new.
   - If `Url` is `None`: look up global source by `Name` and merge local field overrides onto it. Error if not found.
3. Final ordered list = local sources (declared order) + global-only sources not referenced by local (appended at lower priority).
4. `search` uses this order as priority; index 0 = highest priority.

For scalar settings: local non-`None` values win; global defaults fill `None`; ultimate fallback for `CommitOnPull` is `false`.

## Tag-based Pull Semantics (`src/Eru/Config.fs` — `resolveByTags`)

`eru add --tags backend dotnet` (AND semantics — all tags must match):

1. Search `GlobalConfig.Collections` for collections where the collection's `Tags` list contains **all** of the specified tags.
2. For each matching collection, each `CollectionFileRef` is a candidate. A file is included if:
   - The collection matched (its tags cover all requested tags), OR
   - The file's own `Tags` list contains all requested tags (even if the collection alone doesn't match).
3. The result is a deduplicated list of `(SourceName, RemotePath)` tuples to pull.
4. Each pulled file is fetched using the merged source config (branch comes from the source), content-hashed, and written to `eru.lock`.

Tag matching is case-insensitive. Tags are stored without the `#` prefix (stripped at input).

## Serialization (`src/Eru/Serialization.fs`)

**`eru.json` / global config** (JSON via `System.Text.Json`):
- `JsonSerializerOptions` with `PropertyNamingPolicy = CamelCase` and `WriteIndented = true`
- One custom converter: `OptionConverter<'T>` (~20 lines) registered globally — F# `option` maps to `null`/value in JSON
- `JsonIgnore(Condition = WhenWritingNull)` on all option fields

**`eru.lock`** (line-based, custom parser in `LockFile.fs`):
- Parse: skip blank lines and `#` comments; split each line on `\t` into exactly 3 fields; split the second field on the first `:` to recover `SourceName` and `RemotePath`; error on malformed lines
- Write: sort entries by `localPath`, emit version comment first, then one line per entry with `sourceName:remotePath` combined
- No `System.Text.Json` involved

## Platform Path Resolution (`src/Eru/Paths.fs`)

- Global config: `$XDG_CONFIG_HOME/eru/config.json` → fallback `~/.config/eru/config.json` → Windows `%APPDATA%\eru\config.json`
- Local config: `eru.json` relative to current working directory
- Lock file: `eru.lock` relative to current working directory (overridable via `LocalSettings.StateFile`)

## Edge Cases

| Situation | Behavior |
|---|---|
| No `eru.json` | Global-only mode; `add` creates `eru.lock` and warns no local config |
| No global config | Global sources = empty; local config is sole source |
| Both absent | Hard error with "Run `eru init` to get started" |
| `eru.lock` missing | Treated as empty entries list; created on first write |
| Lock entry with unknown source | `sync` warns and skips; no silent data loss |
| `--tags` matches no collections | Informational message: "No collections or files match tags [x, y]" |
| Collection references unknown source | Validation error at load time: "Collection 'x' references unknown source 'y'" |
| Two collections share a file (same source + remotePath) | Deduplication keeps one `LockEntry` per `localPath`; last-write wins on hash |
| `version` > tool supports | Hard error: "please upgrade eru" |
| Duplicate source names in a file | Validation error at parse time |

## Schema Versioning

- `eru.json` and global config carry `"version": 1` (integer). Tool errors on unknown future versions.
- `eru.lock` carries a version in its first comment line: `# eru.lock v1`. Parser reads this to detect schema migrations; missing comment = v1 for backwards compatibility.
- A `Migrations` module handles future upgrades for both formats.

## Files to Create

- `src/Eru/Config.fs` — `SourceConfig`, `CollectionFileRef`, `CollectionConfig`, `GlobalConfig`, `LocalConfig`, `mergeConfigs`, `resolveByTags`
- `src/Eru/LockFile.fs` — `LockEntry`, line-based parser/writer, entry lookup by `LocalPath`
- `src/Eru/Serialization.fs` — `JsonSerializerOptions`, `OptionConverter<'T>`, shared `serialize`/`deserialize`
- `src/Eru/Paths.fs` — platform-aware path resolution
- `tests/Eru.Tests/ConfigTests.fs` — merge semantics, tag resolution, edge cases, version mismatch

## Verification

```bash
# Once implemented:
dotnet build
dotnet test

# Smoke test config loading:
dotnet run --project src/Eru -- init     # creates eru.json
cat eru.json                              # inspect scaffold
dotnet run --project src/Eru -- add <file>  # creates eru.lock
cat eru.lock                              # verify all fields written
```
