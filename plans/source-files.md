# Plan: `eru source files <name>`

## Context

The source manifest supports glob patterns (e.g. `github/*.md`) to describe which files a source exposes. When running `eru source view`, these globs appear verbatim — there is no way to see what concrete files a glob covers. Users cannot tell which paths are available to add without guessing or browsing the remote repo themselves. This command provides a one-shot listing of all concrete files a source exposes, resolved from the manifest patterns.

---

## Approach

Add a `files` subcommand to `eru source`. The command:

1. Reads the source config (URL, branch, optional basePath).
2. Clones the remote shallowly without checkout and runs `git ls-tree -r --name-only HEAD` to get all file paths (one network call). If a `BasePath` is configured, scope the ls-tree to that subdirectory and strip the prefix from results.
3. Reads the cached manifest for the source (no network needed).
4. For each file path returned by ls-tree, checks which manifest entry it matches using `Patterns.matchesGlob`. Files not covered by any manifest entry are omitted.
5. Prints each matched file with the tags and description inherited from its matching manifest entry.

Example output:

```
Files for source: knowledge

  github/apps.md  [github]  — GitHub reference files covering Apps, CLI commands, and Copilot setup.
  github/cli.md  [github]  — GitHub reference files covering Apps, CLI commands, and Copilot setup.
  github/copilot-dotnet-environment.md  [github, copilot]  — How to configure the Copilot ephemeral environment for .NET projects using setup steps.
```

(The explicit manifest entry `github/copilot-dotnet-environment.md [copilot]` and the glob entry `github/*.md [github]` are merged — tags from both matching entries are combined.)

If no manifest is cached, print a note to run `eru sync` first rather than making a second network call.

---

## Files to change

### 1. `src/Eru.Adapters/GitAdapter.fs`

Add `listRemoteFiles` alongside the existing `listRemoteTopLevel` (line 65). Same no-checkout blobless clone pattern, but with a recursive ls-tree:

```fsharp
let listRemoteFiles (url: string) (branch: string option) (basePath: string option) : Result<string list, string> =
    withTempDir (fun tmpDir ->
        let branchArgs = branch |> Option.map (fun b -> $"--branch {b}") |> Option.defaultValue ""
        Command.Run("git", $"clone --filter=blob:none --depth=1 --no-checkout {branchArgs} -- {url} {tmpDir}")
        let treeTarget = basePath |> Option.map (fun bp -> $"HEAD:{bp}") |> Option.defaultValue "HEAD"
        let output = Command.ReadAsync("git", $"ls-tree -r --name-only {treeTarget}", workingDirectory = tmpDir) |> Async.AwaitTask |> Async.RunSynchronously
        let prefix = basePath |> Option.map (fun bp -> bp.TrimEnd('/') + "/") |> Option.defaultValue ""
        output.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        |> Array.map (fun f -> if prefix <> "" then prefix + f else f)
        |> Array.toList
        |> Ok)
```

> Note: use the exact same `Command.Run` / `Command.ReadAsync` (SimpleExec) patterns already used by `listRemoteTopLevel` at line 65.

### 2. `src/Eru.Domain/Deps.fs`

Add one field to the `Deps` record:

```fsharp
ListRemoteFiles : string -> string option -> string option -> Result<string list, string>
// url -> branch -> basePath -> file paths
```

### 3. `src/Eru.Adapters/AdapterDeps.fs`

Wire the new field in `AdapterDeps.create`:

```fsharp
ListRemoteFiles = GitAdapter.listRemoteFiles
```

### 4. `src/Eru.Cli/Args.fs`

Add `Files` case to `SourceArgs`:

```fsharp
// New args type:
type SourceFilesArgs =
    | [<MainCommand; ExactlyOnce>] Name of sourceName: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Name _ -> "Name of the source."

// In SourceArgs DU:
| [<SubCommand>] Files of ParseResults<SourceFilesArgs>

// In SourceArgs.IArgParserTemplate:
| Files _ -> "List all concrete files exposed by a source, resolving any manifest glob patterns."
```

### 5. `src/Eru.Cli/CommandMapper/CommandMapper.fs`

Add active pattern `(|SourceFilesCmd|_|)` alongside the existing `SourceViewCmd`:

```fsharp
let (|SourceFilesCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Files filesArgs ->
                    Some (filesArgs.GetResult SourceFilesArgs.Name)
                | _ -> None)
        | _ -> None)
```

### 6. `src/Eru.Domain/Source.fs`

Add a `files` function:

```fsharp
let files (deps: Deps) (sourceName: string) : int =
    // resolve source from local + global config (same lookup as `view`)
    match found with
    | None -> eprintfn $"Error: source '{sourceName}' not found."; 1
    | Some src ->
        match deps.ReadCachedManifest src.Name with
        | Ok None | Error _ ->
            eprintfn "No manifest cached for this source. Run 'eru sync' first."
            1
        | Ok (Some manifest) ->
            let url    = src.Url    |> Option.defaultValue ""
            let branch = src.Branch
            match deps.ListRemoteFiles url branch src.BasePath with
            | Error e -> eprintfn $"Error listing remote files: {e}"; 1
            | Ok allFiles ->
                printfn $"Files for source: {src.Name}\n"
                let matched =
                    allFiles
                    |> List.choose (fun path ->
                        let matchingEntries =
                            manifest.Files
                            |> List.filter (fun mf -> Patterns.matchesGlob mf.Path path)
                        if matchingEntries.IsEmpty then None
                        else
                            let tags = matchingEntries |> List.collect _.Tags |> List.distinct
                            let desc = matchingEntries |> List.tryPick _.Description
                            Some (path, tags, desc))
                if matched.IsEmpty then
                    printfn "  (no files matched manifest patterns)"
                else
                    for (path, tags, desc) in matched do
                        let tagStr  = if tags.IsEmpty then "" else $"  [{tags |> String.concat ", "}]"
                        let descStr = desc |> Option.map (fun d -> $"  — {d}") |> Option.defaultValue ""
                        printfn $"  {path}{tagStr}{descStr}"
                0
```

**Reuses:** config lookup from `view` (same `ReadGlobalConfig`/`ReadLocalConfig` pattern), `Patterns.matchesGlob` (`Patterns.fs`), `deps.ReadCachedManifest`.

### 7. `src/Eru.Cli/Program.fs`

Add a match arm alongside `SourceViewCmd`:

```fsharp
| SourceFilesCmd name -> Source.files deps name
```

---

## Verification

```bash
# Build
dotnet build

# List files for a known source (requires cached manifest + network)
dotnet run --project src/Eru -- source files knowledge

# Error: unknown source
dotnet run --project src/Eru -- source files nonexistent

# Error: no manifest cached (delete cache first)
rm ~/.cache/eru/sources/knowledge/manifest.json
dotnet run --project src/Eru -- source files knowledge
# Expect: "Run 'eru sync' first" message

# Run tests
dotnet test
```
