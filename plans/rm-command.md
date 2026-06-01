---
status: done
---
# Plan: `eru remove` command

## Context

There is currently no way to un-track a pulled artifact. Users who want to remove a file must manually delete it from disk **and** hand-edit `.eru/eru.lock`. `eru rm` fills this gap: given a `LocalPath`, it deletes the physical file and removes its lock entry atomically. A `--dryrun` flag previews the action without writing.

## Command signature

```
eru rm <localPath> [--dryrun] [-o <format>]
```

- `<localPath>` — local path of the artifact as recorded in the lock (e.g. `knowledge/adr.md`)
- `--dryrun` — print what would be done without writing anything
- `-o` — output format: table (default), text, json

No `--global` flag (the lock is always local).

## Implementation steps

### 1. Add `DeleteLocalFile` dep

**`src/Eru.Domain/Deps.fs`** — append one field:
```fsharp
DeleteLocalFile : string -> Result<unit, string>
```

**`src/Eru.Adapters/AdapterDeps.fs`** — wire it in `create`:
```fsharp
DeleteLocalFile = fun path ->
    try File.Delete path; Ok ()
    with ex -> Error ex.Message
```

### 2. New domain module — `src/Eru.Domain/Remove.fs`

Template: `ManifestRemove.fs` (same read–check–dryrun–write pattern).

```fsharp
namespace Eru

module Remove =

    type Command = { LocalPath: string; DryRun: bool }

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
        match LockFile.findByLocalPath cmd.LocalPath entries with
        | None -> Error $"'{cmd.LocalPath}' is not tracked in the lock file."
        | Some _ ->
        if cmd.DryRun then
            Ok $"Would remove '{cmd.LocalPath}' from lock and delete file."
        else
            let remaining = entries |> List.filter (fun e -> e.LocalPath <> cmd.LocalPath)
            match deps.WriteLockEntries eff.StateFile remaining with
            | Error e -> Error e
            | Ok () ->
            let fullPath = System.IO.Path.Combine(deps.GetCwd(), cmd.LocalPath)
            match deps.DeleteLocalFile fullPath with
            | Error e -> Error $"Lock entry removed but could not delete file: {e}"
            | Ok () -> Ok $"Removed '{cmd.LocalPath}'."
```

Add `Remove.fs` to **`src/Eru.Domain/Eru.Domain.fsproj`** after `ManifestRemove.fs`:
```xml
<Compile Include="ManifestRemove.fs" />
<Compile Include="Remove.fs" />
<Compile Include="ManifestVerify.fs" />
```

### 3. New CLI args — `src/Eru.Cli/Args.fs`

Add `RmArgs` DU before `McpArgs`:
```fsharp
type RmArgs =
    | [<MainCommand; ExactlyOnce>]       Local_Path of localPath: string
    | [<Unique>]                         Dryrun
    | [<Unique; AltCommandLine("-o")>]   Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Local_Path _ -> "Local path of the artifact to remove (as recorded in the lock file)."
            | Dryrun       -> "Show what would be removed without writing anything."
            | Output _     -> "Output format: table (default), text, json."
```

Add `Rm` case to `EruArgs` before `Mcp`:
```fsharp
| [<SubCommand>] Rm of ParseResults<RmArgs>
```

Add its usage line:
```fsharp
| Rm _ -> "Remove a tracked artifact from disk and the lock file."
```

### 4. New CLI module — `src/Eru.Cli/RmCli.fs`

Template: `ManifestRemoveCli.fs`.

```fsharp
module Eru.Cli.RmCli

open Argu
open Eru
open Eru.Cli.OutputFormat

type Cmd = { Command: Remove.Command; Format: OutputFormat }

let (|RmCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Rm args ->
            Some {
                Command = {
                    Remove.Command.LocalPath = args.GetResult RmArgs.Local_Path
                    Remove.Command.DryRun    = args.Contains  RmArgs.Dryrun
                }
                Format = parseFormat (args.TryGetResult RmArgs.Output)
            }
        | _ -> None)

let run (deps: Eru.Deps) (cmd: Cmd) : int =
    match Remove.execute deps cmd.Command with
    | Error e -> renderError e; 1
    | Ok msg  -> renderMessage msg cmd.Format; 0
```

Add `RmCli.fs` to **`src/Eru.Cli/Eru.Cli.fsproj`** before `Program.fs`:
```xml
<Compile Include="ManifestVerifyCli.fs" />
<Compile Include="RmCli.fs" />
<Compile Include="Program.fs" />
```

### 5. Wire into `Program.fs`

Add `open Eru.Cli.RmCli` to the open list, then add before the catch-all `_`:
```fsharp
| RmCmd cmd -> RmCli.run deps cmd
```

### 6. Tests — `tests/Eru.Tests/RemoveTests.fs`

Follow `ManifestTests.fs` / `SyncTests.fs` stub pattern. Key cases:

- Entry not in lock → `Error` containing "not tracked"
- `--dryrun` with matching entry → `Ok` "Would remove", no writes
- Happy path → lock written without the entry, `DeleteLocalFile` called with full resolved path
- `WriteLockEntries` fails → propagates `Error`
- `DeleteLocalFile` fails → `Error` noting the lock entry was already removed

Register in **`tests/Eru.Tests/Eru.Tests.fsproj`** after `ManifestTests.fs`.

## Verification

```bash
dotnet build
dotnet test

# Smoke (needs a repo with an entry in .eru/eru.lock)
dotnet run --project src/Eru -- rm knowledge/adr.md --dryrun
dotnet run --project src/Eru -- rm knowledge/adr.md
```

After the real run:
1. The file no longer exists on disk.
2. `.eru/eru.lock` no longer contains the entry.
3. `eru rm nonexistent/path.md` exits non-zero with an error message.
