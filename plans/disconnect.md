# Plan: `eru disconnect` command

## Context

`eru remove` deletes a single tracked artifact from disk and removes its lock entry. There is no command to remove a lock entry for a single file while keeping the local file intact. `eru disconnect` fills that gap: same target resolution as `remove` (local path or path short hash), removes the matching lock entry, but does **not** delete the local file or touch the source config. This is useful when you want to keep a local copy but stop tracking it with eru.

---

## Approach

Mirror `Remove` exactly, minus the `deps.DeleteLocalFile` call. The `resolveEntry` helper is copied into `Disconnect.fs` (consistent with how `remove` isolates its own resolution logic).

**Behaviour:**
- Target: local path or path short hash (same as `remove`)
- Removes the single matching lock entry
- Does **not** delete the file from disk
- DryRun: `"Would disconnect '{localPath}' from lock."`
- Success: `"Disconnected '{localPath}'."`
- No match / ambiguous match: same error messages as `remove`

---

## Files to create

### `src/Eru.Domain/Disconnect.fs`

```fsharp
namespace Eru

module Disconnect =

    type Command = {
        Target : string
        DryRun : bool
    }

    let private resolveEntry (target: string) (entries: LockEntry list) : Result<LockEntry, string> =
        let byHash = entries |> List.filter (fun e -> (Patterns.pathShortHash e.RemotePath).StartsWith target)
        let byPath = entries |> List.filter (fun e -> e.LocalPath = target)
        let matches = (byHash @ byPath) |> List.distinctBy (fun e -> e.LocalPath)
        match matches with
        | []  -> Error $"'{target}' did not match any tracked file."
        | [e] -> Ok e
        | _   -> Error $"'{target}' matched {matches.Length} files, be more specific."

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->
        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->
        match deps.ReadLockEntries eff.StateFile with
        | Error e -> Error $"Error reading lock file: {e}"
        | Ok entries ->
        match resolveEntry cmd.Target entries with
        | Error e -> Error e
        | Ok entry ->
        if cmd.DryRun then
            Ok $"Would disconnect '{entry.LocalPath}' from lock."
        else
            let remaining = entries |> List.filter (fun e -> e.LocalPath <> entry.LocalPath)
            match deps.WriteLockEntries eff.StateFile remaining with
            | Error e -> Error e
            | Ok () -> Ok $"Disconnected '{entry.LocalPath}'."
```

### `src/Eru.Cli/DisconnectCli.fs`

```fsharp
module Eru.Cli.DisconnectCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: Disconnect.Command; Format: OutputFormat }

let (|DisconnectCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Disconnect args ->
            Some {
                Command = {
                    Disconnect.Command.Target = args.GetResult DisconnectArgs.Target
                    Disconnect.Command.DryRun = args.Contains  DisconnectArgs.Dryrun
                }
                Format = parseFormat (args.TryGetResult DisconnectArgs.Output)
            }
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Disconnect.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
```

### `tests/Eru.Tests/DisconnectTests.fs`

Tests following the same pattern as `RemoveTests.fs`:
- Matching entry by local path → lock entry removed, no DeleteLocalFile call
- Matching entry by short hash → same
- DryRun → returns "Would disconnect" message, WriteLockEntries never called
- Unknown target → returns Error
- Ambiguous target → returns Error

---

## Files to modify

### `src/Eru.Cli/Args.fs`

Add before `McpArgs`:
```fsharp
type DisconnectArgs =
    | [<MainCommand; ExactlyOnce>]     Target of target: string
    | [<Unique>]                       Dryrun
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Target _ -> "Local path or path short hash of the artifact to disconnect."
            | Dryrun   -> "Show what would be disconnected without writing anything."
            | Output _ -> "Output format: table (default), text, json."
```

Add to `EruArgs` DU (after `Remove`):
```fsharp
| [<SubCommand>] Disconnect of ParseResults<DisconnectArgs>
```

Add to `EruArgs.Usage`:
```fsharp
| Disconnect _ -> "Remove a tracked artifact from the lock file without deleting the local file."
```

### `src/Eru.Domain/Eru.Domain.fsproj`

Add after `<Compile Include="Remove.fs" />`:
```xml
<Compile Include="Disconnect.fs" />
```

### `src/Eru.Cli/Eru.Cli.fsproj`

Add after `<Compile Include="RemoveCli.fs" />` (before `Program.fs`):
```xml
<Compile Include="DisconnectCli.fs" />
```

### `src/Eru.Cli/Program.fs`

Add `open Eru.Cli.DisconnectCli` with the other opens.

Add dispatch case after `RemoveCmd`:
```fsharp
| DisconnectCmd cmd -> DisconnectCli.run deps cmd
```

### `tests/Eru.Tests/Eru.Tests.fsproj`

Add `<Compile Include="DisconnectTests.fs" />` after `RemoveTests.fs`.

---

## Verification

1. `dotnet build src/Eru.Cli` — compiles cleanly
2. `dotnet test tests/Eru.Tests` — all tests pass including new DisconnectTests
3. Manual smoke test:
   - `eru disconnect knowledge/adr.md --dryrun` — shows message, lock unchanged
   - `eru disconnect knowledge/adr.md` — lock entry removed, file still on disk
   - `eru disconnect nonexistent` — exits 1 with error
   - `eru disconnect knowledge/adr.md --output json` — JSON output
