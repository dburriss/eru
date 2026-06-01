---
status: done
---
# Plan: `eru source view <source>`

## Context

Users need a way to inspect a single source in detail — its config properties and the files it exposes (from its cached manifest). The existing `eru source list` shows a brief summary of all sources; `view` zooms in on one, giving a richer picture without requiring a network fetch.

---

## Approach

Add a `view` subcommand to `eru source`, mirroring how `list` is wired. The command:
1. Resolves the named source from local + global config (same lookup as `list`).
2. Prints all its details (name, url, branch, basepath, scope).
3. Reads the cached manifest and prints each `ManifestFileRef` (path, tags, description).
4. Caps at 20 entries unless `--full` is passed.
5. If no manifest is cached, prints a note to run `eru sync`.

---

## Files to change

### 1. `src/Eru.Cli/Args.fs`

Add `SourceViewArgs` (with a positional `Name` and a `Full` flag) and a `View` case to `SourceArgs`.

```fsharp
type SourceViewArgs =
    | [<MainCommand; ExactlyOnce>] Name of sourceName: string
    | [<Unique>]                   Full
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _ -> "Name of the source to view."
            | Full   -> "Show all files without the 20-entry cap."

// In SourceArgs DU, add:
| [<SubCommand>] View of ParseResults<SourceViewArgs>

// In SourceArgs.IArgParserTemplate, add:
| View _ -> "Show details and available files for a source."
```

### 2. `src/Eru.Cli/CommandMapper/CommandMapper.fs`

Add an active pattern `(|SourceViewCmd|_|)`:

```fsharp
let (|SourceViewCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.View viewArgs ->
                    Some (viewArgs.GetResult SourceViewArgs.Name,
                          viewArgs.Contains SourceViewArgs.Full)
                | _ -> None)
        | _ -> None)
```

### 3. `src/Eru.Domain/Source.fs`

Add a `view` function. Logic:

```fsharp
let view (deps: Deps) (sourceName: string) (showFull: bool) : int =
    match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
    | Error e, _ | _, Error e -> eprintfn $"Error: {e}"; 1
    | Ok globalCfg, Ok localCfg ->
        let globalSources = globalCfg |> Option.map _.DefaultSources |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map _.Sources        |> Option.defaultValue []

        let found =
            localSources  |> List.tryFind (fun s -> s.Name = sourceName) |> Option.map (fun s -> s, "local")
            |> Option.orElseWith (fun () ->
                globalSources |> List.tryFind (fun s -> s.Name = sourceName) |> Option.map (fun s -> s, "global"))

        match found with
        | None ->
            eprintfn $"Error: source '{sourceName}' not found."
            1
        | Some (src, origin) ->
            // Print details
            printfn $"Name:     {src.Name}"
            printfn $"Scope:    {origin}"
            src.Url      |> Option.iter (fun u -> printfn $"URL:      {u}")
            src.Branch   |> Option.iter (fun b -> printfn $"Branch:   {b}")
            src.BasePath |> Option.iter (fun p -> printfn $"BasePath: {p}")

            // Print manifest files
            match deps.ReadCachedManifest src.Name with
            | Error e ->
                eprintfn $"Warning: could not read manifest: {e}"
            | Ok None ->
                printfn "\nNo manifest cached. Run 'eru sync' to fetch source metadata."
            | Ok (Some manifest) ->
                let cap      = 20
                let files    = manifest.Files
                let display  = if showFull then files else files |> List.truncate cap
                let total    = files.Length
                printfn $"\nFiles ({total} total{if not showFull && total > cap then $", showing {cap}" else ""}):"
                for f in display do
                    let tags = if f.Tags.IsEmpty then "" else $"  [{f.Tags |> String.concat \", \"}]"
                    let desc = f.Description |> Option.map (fun d -> $"  — {d}") |> Option.defaultValue ""
                    printfn $"  {f.Path}{tags}{desc}"
                if not showFull && total > cap then
                    printfn $"  ... and {total - cap} more (pass --full to see all)"
            0
```

### 4. `src/Eru.Cli/Program.fs`

Add a match arm before the catch-all:

```fsharp
| SourceViewCmd (name, full) -> Source.view deps name full
```

---

## Verification

```bash
# Build
dotnet build

# List sources to get a valid name
dotnet run --project src/Eru -- source list

# View a known source (capped)
dotnet run --project src/Eru -- source view <sourceName>

# View with all files
dotnet run --project src/Eru -- source view <sourceName> --full

# Error case: unknown source
dotnet run --project src/Eru -- source view nonexistent

# Run tests
dotnet test
```
