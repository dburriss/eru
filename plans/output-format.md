# Plan: Add --output format modes to all commands

## Context

All CLI commands previously mixed computation with `printfn` calls in domain modules, and subcommands were grouped as functions on a parent module (`Source.list`, `Collection.create`, etc.). Two structural changes were made together:

1. **Output format support** — `--output <format>` (short: `-o`) on each subcommand that produces output, defaulting to `table`. Spectre.Console renders tables.
2. **Per-subcommand modules** — each command and subcommand has its own module in both domain and CLI layers.

---

## Key decisions

- `--output table|text|json` (short: `-o`) on each subcommand's own args type — **not** on the top-level `EruArgs`.
- Default format: `table`.
- Format is embedded in a CLI-layer `Cmd` record in each `*Cli.fs` module — no tuple returns, no separate format parameter on `run`.
- Domain functions that return data are named `execute`; simple mutations return `Result<string, string>`.
- `CommandMapper.fs` is deleted — active patterns live in each `*Cli.fs`.
- `Source.fs`, `Collection.fs`, `Manifest.fs` domain files are deleted; replaced by per-subcommand modules.
- Each subcommand module defines its own types (no sharing across siblings).

---

## Domain restructure

### Deleted domain files
- `Source.fs`, `Collection.fs`, `Manifest.fs` — replaced by per-subcommand modules below.

### Modified domain files

**`Search.fs`** — `run` → `execute : Deps -> Query -> Result<SearchResult list, string>`

**`Sync.fs`** — adds public result types; `run` → `execute : Deps -> Options -> Result<SyncResult, string>`
```fsharp
type SyncStatus = Current | Drifted | Missing | Skipped of string | Blocked
type SyncEntry  = { Status: SyncStatus; LocalPath: string }
type SyncResult = { Entries: SyncEntry list; DryRun: bool }
```

**`Add.fs`** — adds `PullEntry`; `run` → `execute : Deps -> Command -> Result<PullEntry list, string>`
```fsharp
type PullEntry = Pulled of LockEntry | Blocked of string
```

**`Init.fs`** — `execute : Deps -> Command -> Result<string, string>`

### New domain files

| File | Own types | Signature |
|---|---|---|
| `SourceList.fs` | `SourceRow` | `execute : Deps -> Result<SourceRow list, string>` |
| `SourceView.fs` | `SourceFileEntry`, `SourceDetail`, `ManifestState` | `execute : Deps -> string -> bool -> Result<SourceDetail, string>` |
| `SourceFiles.fs` | `SourceFileRow` | `execute : Deps -> string option -> Result<(string * SourceFileRow list) list, string>` |
| `SourceAdd.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `SourceRemove.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `CollectionCreate.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `CollectionAddFile.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `CollectionRemoveFile.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `ManifestInit.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `ManifestAdd.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `ManifestRemove.fs` | `Command` | `execute : Deps -> Command -> Result<string, string>` |
| `ManifestVerify.fs` | `VerifyResult` | `execute : Deps -> Result<VerifyResult, string>` |

---

## CLI restructure

### Deleted CLI files
- `CommandMapper/CommandMapper.fs` — active patterns distributed into `*Cli.fs` modules.

### `src/Eru.Cli/OutputFormat.fs`

```fsharp
module Eru.Cli.OutputFormat

type OutputFormat = Text | Json | Table

let parseFormat (s: string option) : OutputFormat =
    match s with
    | None -> Table
    | Some f ->
        match f.ToLowerInvariant() with
        | "json" -> Json
        | "text" -> Text
        | _      -> Table

let renderError   (msg: string) = eprintfn "Error: %s" msg
let renderMessage (msg: string) (format: OutputFormat) = ...
let makeTable     (headers: string list) : Spectre.Console.Table = ...
```

### Per-subcommand args: `--output` flag

Each subcommand args type (except `McpArgs`) gains:
```fsharp
| [<Unique; AltCommandLine("-o")>] Output of format: string
```
with usage `"Output format: table (default), text, json."`.

`EruArgs` has **no** format flags — only `--debug`.

### Per-subcommand CLI modules

Each `*Cli.fs` has:
1. **`type Cmd`** — a CLI-layer record that bundles the domain command and `Format: OutputFormat`.
2. **Active pattern** — builds the `Cmd` record, calling `parseFormat (args.TryGetResult ..Output)`.
3. **Private render functions** — one per format (text / json / table).
4. **`run (deps: Deps) (cmd: Cmd) : int`** — calls domain `execute`, dispatches to render.

```fsharp
// Example: SearchCli.fs
type Cmd = { Query: Search.Query; Format: OutputFormat }

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args ->
            Some {
                Query  = { Terms = ...; Tags = ... }
                Format = parseFormat (args.TryGetResult SearchArgs.Output)
            }
        | _ -> None)

let run (deps: Deps) (cmd: Cmd) : int =
    match Search.execute deps cmd.Query with
    | Error e -> renderError e; 1
    | Ok results ->
        match cmd.Format with
        | Text  -> renderText results
        | Json  -> renderJson results
        | Table -> renderTable results
        0
```

| CLI file | Active pattern | Domain call | Table columns |
|---|---|---|---|
| `SearchCli.fs` | `(|SearchCmd|_|)` | `Search.execute` | Source \| Path \| Tags \| Local \| Description |
| `SyncCli.fs` | `(|SyncCmd|_|)` | `Sync.execute` | Status \| Path \| Reason + summary |
| `AddCli.fs` | `(|AddCmd|_|)` | `Add.execute` | Action \| Remote Path \| Local Path |
| `InitCli.fs` | `(|InitCmd|_|)` | `Init.execute` → `renderMessage` | n/a |
| `SourceListCli.fs` | `(|SourceListCmd|_|)` | `SourceList.execute` | Name \| URL \| Branch \| BasePath \| Scope \| Tags |
| `SourceViewCli.fs` | `(|SourceViewCmd|_|)` | `SourceView.execute` | meta key-value + files sub-table |
| `SourceFilesCli.fs` | `(|SourceFilesCmd|_|)` | `SourceFiles.execute` | Source \| Hash \| Path \| Tags \| Description |
| `SourceAddCli.fs` | `(|SourceAddCmd|_|)` | `SourceAdd.execute` → `renderMessage` | n/a |
| `SourceRemoveCli.fs` | `(|SourceRemoveCmd|_|)` | `SourceRemove.execute` → `renderMessage` | n/a |
| `CollectionCreateCli.fs` | `(|CollectionCreateCmd|_|)` | `CollectionCreate.execute` → `renderMessage` | n/a |
| `CollectionAddFileCli.fs` | `(|CollectionAddFileCmd|_|)` | `CollectionAddFile.execute` → `renderMessage` | n/a |
| `CollectionRemoveFileCli.fs` | `(|CollectionRemoveFileCmd|_|)` | `CollectionRemoveFile.execute` → `renderMessage` | n/a |
| `ManifestInitCli.fs` | `(|ManifestInitCmd|_|)` | `ManifestInit.execute` → `renderMessage` | n/a |
| `ManifestAddCli.fs` | `(|ManifestAddCmd|_|)` | `ManifestAdd.execute` → `renderMessage` | n/a |
| `ManifestRemoveCli.fs` | `(|ManifestRemoveCmd|_|)` | `ManifestRemove.execute` → `renderMessage` | n/a |
| `ManifestVerifyCli.fs` | `(|ManifestVerifyCmd|_|)` | `ManifestVerify.execute` | Status \| Path (missing only) |

### `src/Eru.Cli/Program.fs`

No format extraction — each `Cmd` already carries its format:

```fsharp
match parsed with
| McpCmd ()                   -> Eru.Mcp.Server.run deps |> ...; 0
| InitCmd cmd                 -> InitCli.run deps cmd
| AddCmd cmd                  -> AddCli.run deps cmd
| SearchCmd cmd               -> SearchCli.run deps cmd
| SyncCmd cmd                 -> SyncCli.run deps cmd
| SourceListCmd cmd           -> SourceListCli.run deps cmd
| SourceViewCmd cmd           -> SourceViewCli.run deps cmd
| SourceFilesCmd cmd          -> SourceFilesCli.run deps cmd
| SourceAddCmd cmd            -> SourceAddCli.run deps cmd
| SourceRemoveCmd cmd         -> SourceRemoveCli.run deps cmd
| CollectionCreateCmd cmd     -> CollectionCreateCli.run deps cmd
| CollectionAddFileCmd cmd    -> CollectionAddFileCli.run deps cmd
| CollectionRemoveFileCmd cmd -> CollectionRemoveFileCli.run deps cmd
| ManifestInitCmd cmd         -> ManifestInitCli.run deps cmd
| ManifestAddCmd cmd          -> ManifestAddCli.run deps cmd
| ManifestRemoveCmd cmd       -> ManifestRemoveCli.run deps cmd
| ManifestVerifyCmd cmd       -> ManifestVerifyCli.run deps cmd
| _ -> printfn "%s" (parser.PrintUsage()); 0
```

---

## Verification

```bash
dotnet build
dotnet test

# Default table output
dotnet run --project src/Eru.Cli -- source list
dotnet run --project src/Eru.Cli -- search
dotnet run --project src/Eru.Cli -- sync --dryrun

# Explicit formats
dotnet run --project src/Eru.Cli -- source list --output json
dotnet run --project src/Eru.Cli -- source list --output text
dotnet run --project src/Eru.Cli -- search -o json
dotnet run --project src/Eru.Cli -- sync --dryrun --output text
```
