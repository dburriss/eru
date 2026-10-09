---
status: done
---
# Plan: `eru print <source:path | hash>`

## Context
There is no way to dump a document's contents from the CLI. `eru search` shows `[hash: ...]` and
`eru source files` lists hashes, but you must `eru add` a file before reading it. `print` takes a full
`source:path` or a short path-hash and writes the document to stdout (pipe-friendly).

Decisions (confirmed): resolve from source indexes (+ local/cache, live fetch fallback); raw content by
default with `-o json`; hash = 3-8 hex prefix of `Patterns.pathShortHash`, ambiguity is an error listing candidates.

## Approach

### Domain: `src/Eru.Domain/Read.fs` (new, add to `Eru.Domain.fsproj` after `Disconnect.fs`)
`execute (deps: Deps) (target: string) : Result<Document, string>` where
`Document = { Source; RemotePath; Hash; Content }`.

1. Load effective config (same boilerplate as `Remove.execute`: `ReadGlobalConfig`/`ReadLocalConfig` -> `Config.merge`).
2. Resolve target to `(source, remotePath)`:
   - Contains `:` -> `EntryId.tryParse` (`Config.fs`). Verify source exists in `eff.Sources`.
   - Else if `isShortHash` -> scan `deps.ReadSourceIndex` for each source's `Entries` keys,
     keep those where `Patterns.pathShortHash p` starts with the prefix (pattern from `Search.fs:61-78`).
     0 matches -> "no file found for hash prefix '...'"; >1 -> "ambiguous short hash '...' - n files match" listing `source:path`.
   - Else error with usage hint.
   - Move `isShortHash` out of `Add.fs:63` into `Patterns.fs` so both share it (update Add to use it).
3. Read content, in order:
   - `entry.LocalPath` set -> `deps.ReadLocalFile` (resolve against `deps.GetCwd()` with `PathJoin.Combine`).
   - `entry.CacheRelPath` set -> `deps.ReadCachedSourceContent source rel`.
   - Fallback: `deps.FetchRemoteContent url branch [remotePath]`, then `Patterns.isBlocked` check (as in `McpTools.fs:160-227`).
   - For an explicit `source:path` not in the index, go straight to the live fetch.
4. Use `PathJoin.Combine`/`PathUtil.*` only (enforced by `DomainPathConventionTests`).

### CLI
- `src/Eru.Cli/Args.fs`: add `PrintArgs` (`[<MainCommand; ExactlyOnce>] Target`, `[<Unique; AltCommandLine("-o")>] Output`), register `| [<SubCommand>] Print of ParseResults<PrintArgs>` in `EruArgs` plus Usage text.
- `src/Eru.Cli/PrintCli.fs` (new, modelled on `RemoveCli.fs`): `Cmd`, `(|PrintCmd|_|)`, `run`. Text writes content raw to stdout with no trailing decoration; Json emits `{source, path, hash, content}`; errors via `renderError` to stderr, exit 1. Add to `Eru.Cli.fsproj` before `Program.fs`.
- `src/Eru.Cli/Program.fs`: `open Eru.Cli.PrintCli` and dispatch `| PrintCmd cmd -> PrintCli.run deps cmd`.
  Since `parseFormat` defaults to Table, map the default to Text for this command.

### Tests: `tests/Eru.Tests/ReadTests.fs` (new, register in `Eru.Tests.fsproj`)
Copy the `makeDeps` stub pattern from `RemoveTests.fs`. Cases: `source:path` from cache; from local file; live-fetch fallback;
blocked content; unique hash prefix; no match; ambiguous hash across sources; unknown source; path containing `:`; invalid input.

### Docs
- `CHANGELOG.md` under `[Unreleased]` -> `### Added` (required by AGENTS.md).
- `README.md` command table; `docs/reference/cli.md` (`## eru print`); `docs/reference/inspecting-state-and-search.md` table.
- `skills/eru/SKILL.md` and `skills/eru/references/commands.md`.
- `plans/print.md` with `status:` frontmatter (follow `plans/source-files-short-hash.md`).

### MCP refactor: `src/Eru.Mcp/McpTools.fs` `KnowledgeTools.Read` (lines ~160-227)
Confirmed in scope. Split `Read` into two reusable functions so MCP and CLI share the content-reading path:
- `Read.resolveTarget` (source:path / hash -> `(source, remotePath)`)
- `Read.readContent deps eff source remotePath` (local file -> cache -> live fetch + blocklist check)

`read_artifact` keeps its MCP-specific lookups (local filesystem path relative to cwd, lock `LocalPath`,
`"{src}/{remotePath}"` and bare remotePath index keys) and delegates the cache/live-fetch steps to `Read.readContent`.
It gains hash support for free via `resolveTarget`. Preserve its existing behaviour of returning errors as plain text strings.
The MCP project must go through `Deps` instead of direct `File.ReadAllText` where it now delegates.
Add regression tests for the existing `read_artifact` resolution orders if any exist; otherwise cover via `ReadTests`.
Update `docs/reference/mcp-server.md` and `skills/eru/references/mcp.md` to mention hash support.

## Out of scope
Changing `read_artifact`'s public tool signature or output format.

## Verification
- `dotnet test --solution eru.slnx -- --filter-method "*ReadTests*"`, then the full suite.
- Manual: `dotnet run --project src/Eru.Cli -- print <source:path>`, then with a hash taken from `eru search`/`eru source files`,
  then `-o json`, an ambiguous or unknown hash (exit 1, stderr), and piping (`| head`).
