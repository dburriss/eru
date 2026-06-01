---
status: done
---
# Plan: Add `source remove` and `collection remove` subcommands

## Context

`source` and `collection` currently have no remove commands, leaving users unable to delete a source or drop a file reference from a collection through the CLI. `manifest` already has a working `remove` subcommand that establishes the exact pattern to follow. This change adds parity.

- `eru source remove <name>` — removes a named source from local (or global with `--global`) config  
- `eru collection remove <collection> -f <source:path>` — removes a single file reference from an existing collection (mirrors `collection add`)

---

## Files to modify

| File | Change |
|---|---|
| `src/Eru.Cli/Args.fs` | Add `SourceRemoveArgs`, extend `SourceArgs`; add `CollectionRemoveFileArgs`, extend `CollectionArgs` |
| `src/Eru.Domain/Source.fs` | Add `RemoveCommand` record + `remove` function |
| `src/Eru.Domain/Collection.fs` | Add `RemoveFileCommand` record + `removeFile` function |
| `src/Eru.Cli/CommandMapper/CommandMapper.fs` | Add `SourceRemoveCmd` and `CollectionRemoveFileCmd` active patterns |
| `src/Eru.Cli/Program.fs` | Wire the two new active patterns to their domain functions |
| `tests/Eru.Tests/SourceTests.fs` | Add `remove` tests |
| `tests/Eru.Tests/CollectionTests.fs` | Create file with `removeFile` tests (no collection test file exists yet) |

---

## Step-by-step implementation

### 1. `src/Eru.Cli/Args.fs`

**`source remove`** — add before `SourceArgs`:
```fsharp
type SourceRemoveArgs =
    | [<MainCommand; ExactlyOnce>] Name   of name: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _  -> "Name of the source to remove."
            | Global  -> "Remove from global config (~/.config/eru/config.json)."
            | Dryrun  -> "Show what would be removed without writing anything."
```

Extend `SourceArgs` DU:
```fsharp
| [<SubCommand>] Remove of ParseResults<SourceRemoveArgs>
// usage: "Remove a knowledge source."
```

**`collection remove`** — add before `CollectionArgs`:
```fsharp
type CollectionRemoveFileArgs =
    | [<MainCommand; ExactlyOnce>] Collection   of name: string
    | [<AltCommandLine("-f"); ExactlyOnce>] File of sourceAndPath: string
    | [<AltCommandLine("-g")>]     Global
    | [<Unique>]                   Dryrun
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Collection _  -> "Name of the collection."
            | File _        -> "File reference to remove as source:remotePath."
            | Global        -> "Write to global config (~/.config/eru/config.json)."
            | Dryrun        -> "Show what would be removed without writing anything."
```

Extend `CollectionArgs` DU:
```fsharp
| [<SubCommand>] Remove of ParseResults<CollectionRemoveFileArgs>
// usage: "Remove a file reference from an existing collection."
```

---

### 2. `src/Eru.Domain/Source.fs`

Add command record and function (follow the same global/local split as `Source.add`):

```fsharp
type RemoveCommand = {
    Name     : string
    IsGlobal : bool
    DryRun   : bool
}

let remove (deps: Deps) (cmd: RemoveCommand) : int =
    if cmd.IsGlobal then
        match deps.ReadGlobalConfig () with
        | Error e -> eprintfn $"Error: {e}"; 1
        | Ok None -> eprintfn "Error: no global config found."; 1
        | Ok (Some g) ->
            if not (g.DefaultSources |> List.exists (fun s -> s.Name = cmd.Name)) then
                eprintfn $"Error: source '{cmd.Name}' not found in global config."; 1
            elif cmd.DryRun then
                printfn $"Would remove source '{cmd.Name}' from global config."; 0
            else
                let updated = { g with DefaultSources = g.DefaultSources |> List.filter (fun s -> s.Name <> cmd.Name) }
                match deps.WriteGlobalConfig updated with
                | Ok ()   -> printfn $"Removed source '{cmd.Name}' from global config."; 0
                | Error e -> eprintfn $"Error: {e}"; 1
    else
        match deps.ReadLocalConfig () with
        | Error e -> eprintfn $"Error: {e}"; 1
        | Ok None -> eprintfn "Error: no .eru/config.json found. Run 'eru init' first."; 1
        | Ok (Some local) ->
            if not (local.Sources |> List.exists (fun s -> s.Name = cmd.Name)) then
                eprintfn $"Error: source '{cmd.Name}' not found in .eru/config.json."; 1
            elif cmd.DryRun then
                printfn $"Would remove source '{cmd.Name}' from .eru/config.json."; 0
            else
                let updated = { local with Sources = local.Sources |> List.filter (fun s -> s.Name <> cmd.Name) }
                match deps.WriteLocalConfig updated with
                | Ok ()   -> printfn $"Removed source '{cmd.Name}' from .eru/config.json."; 0
                | Error e -> eprintfn $"Error: {e}"; 1
```

---

### 3. `src/Eru.Domain/Collection.fs`

Add command record and function (parallel to `Collection.addFile`):

```fsharp
type RemoveFileCommand = {
    CollectionName : string
    Source         : string
    RemotePath     : string
    IsGlobal       : bool
    DryRun         : bool
}
```

Follow the exact same global/local dispatch pattern as `Collection.addFile` (lines 63–114 of Collection.fs). The identity of a file ref is `(Source, RemotePath)` — the same pair used for deduplication in `addFile`.

After filtering the file ref out, check if the resulting `Files` list is empty. If so, remove the entire `CollectionConfig` entry from the config (not just leave an empty collection). Print a message like `"Removed last file from collection 'name'; collection entry removed."` in this case.

---

### 4. `src/Eru.Cli/CommandMapper/CommandMapper.fs`

Add two active patterns, following the `ManifestRemoveCmd` pattern (two-level drill into `EruArgs`):

```fsharp
let (|SourceRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Remove removeArgs ->
                    Some { Source.RemoveCommand.Name     = removeArgs.GetResult SourceRemoveArgs.Name
                           Source.RemoveCommand.IsGlobal = removeArgs.Contains SourceRemoveArgs.Global
                           Source.RemoveCommand.DryRun   = removeArgs.Contains SourceRemoveArgs.Dryrun }
                | _ -> None)
        | _ -> None)

let (|CollectionRemoveFileCmd|_|) (r: ParseResults<EruArgs>) =
    // same pattern — drill into CollectionArgs.Remove, parse Collection + File + Global + Dryrun
    // split the File arg on ':' to get Source and RemotePath (same as CollectionAddFileCmd)
```

---

### 5. `src/Eru.Cli/Program.fs`

Add two lines to the `match parsed with` block, following the existing source/collection lines:

```fsharp
| SourceRemoveCmd cmd          -> Source.remove      deps cmd
| CollectionRemoveFileCmd cmd  -> Collection.removeFile deps cmd
```

---

### 6. Tests

**`tests/Eru.Tests/SourceTests.fs`** — add a `removeTests` list:
- `remove removes source from local config` — verify remaining sources after removal
- `remove fails when source not found` — exit code 1, no write
- `remove dryrun does not write` — exit code 0, captured config is None
- `remove fails when no local config` — exit code 1

Use the existing `makeDeps`-style stub pattern from SourceTests.fs.

**`tests/Eru.Tests/CollectionTests.fs`** — create new file with `removeFileTests`:
- `removeFile removes matching file ref` — verify collection after removal
- `removeFile fails when collection not found` — exit code 1
- `removeFile fails when file ref not found` — exit code 1
- `removeFile dryrun does not write` — exit code 0, no write
- `removeFile removes collection entry when last file is removed` — after removal, the collection is absent from the written config

---

## Verification

```bash
# Build
dotnet build eru.slnx

# Tests
dotnet test eru.slnx

# Manual smoke test (local source remove)
eru source add https://github.com/example/repo
eru source list               # confirm it's there
eru source remove example/repo --dryrun
eru source remove example/repo
eru source list               # confirm it's gone

# Manual smoke test (collection remove file)
eru collection create my-col
eru collection add my-col -f some-source:docs/guide.md
eru collection remove my-col -f some-source:docs/guide.md --dryrun
eru collection remove my-col -f some-source:docs/guide.md
```
