---
status: done
---
# Plan: `eru source list` command

## Context

`eru source add` lets users register knowledge sources, but there is no way to inspect which sources are currently configured. This plan adds `eru source list` to print all configured sources, showing their name, URL, branch, basePath, and whether they originate from local or global config.

---

## Affected files

| File | Change |
|------|--------|
| `src/Eru.Cli/Args.fs` | Add `SourceListArgs` type and `List` case to `SourceArgs` |
| `src/Eru.Cli/CommandMapper/CommandMapper.fs` | Add `SourceListCmd` pattern; fix `SourceAddCmd` to handle new `List` case |
| `src/Eru.Cli/Program.fs` | Dispatch `SourceListCmd → Source.list deps` |
| `src/Eru.Domain/Source.fs` | Implement `Source.list` function |
| `tests/Eru.Tests/SourceTests.fs` | Add tests for `Source.list` |

---

## Implementation

### 1. `Args.fs` — add args type and extend `SourceArgs`

Add a minimal args type for the `list` subcommand (no arguments currently needed):

```fsharp
type SourceListArgs =
    | [<Hidden>] Placeholder
    interface IArgParserTemplate with
        member a.Usage = match a with Placeholder -> ""
```

Extend `SourceArgs` to include the new subcommand:

```fsharp
[<CliPrefix(CliPrefix.None)>]
type SourceArgs =
    | [<SubCommand>] Add  of ParseResults<SourceAddArgs>
    | [<SubCommand>] List of ParseResults<SourceListArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Add  _ -> "Add a new knowledge source."
            | List _ -> "List configured knowledge sources."
```

### 2. `CommandMapper.fs` — add pattern; fix existing pattern

Add the new active pattern:

```fsharp
let (|SourceListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.List _ -> Some ()
                | _ -> None)
        | _ -> None)
```

Fix `SourceAddCmd` — the inner `Option.map` with a partial `SourceArgs.Add` pattern will throw a `MatchFailureException` at runtime once `List` is a valid subcommand. Change to `Option.bind`:

```fsharp
let (|SourceAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Add addArgs ->
                    Some {
                        Url      = addArgs.GetResult   SourceAddArgs.Url
                        Name     = addArgs.TryGetResult SourceAddArgs.Name
                        Branch   = addArgs.TryGetResult SourceAddArgs.Branch
                        BasePath = addArgs.TryGetResult SourceAddArgs.Basepath
                        IsGlobal = addArgs.Contains    SourceAddArgs.Global
                    }
                | _ -> None)
        | _ -> None)
```

### 3. `Program.fs` — dispatch

```fsharp
| SourceListCmd    -> Source.list deps
| SourceAddCmd cmd -> Source.add  deps cmd
```

`SourceListCmd` must appear before `SourceAddCmd` since both match on `EruArgs.Source`.

### 4. `Source.fs` — implement `list`

```fsharp
let list (deps: Deps) : int =
    match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
    | Error e, _ | _, Error e -> eprintfn $"Error: {e}"; 1
    | Ok globalCfg, Ok localCfg ->
        let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []
        let localNames    = localSources |> List.map (fun s -> s.Name) |> Set.ofList

        let fmt (src: SourceConfig) (origin: string) =
            let url      = src.Url      |> Option.defaultValue "(inherits from global)"
            let branch   = src.Branch   |> Option.map (fun b -> $" [branch: {b}]")   |> Option.defaultValue ""
            let basePath = src.BasePath |> Option.map (fun p -> $" [basepath: {p}]") |> Option.defaultValue ""
            printfn $"  {src.Name}  {url}{branch}{basePath}  [{origin}]"

        // Local sources first
        for src in localSources do
            let origin = if src.Url.IsSome then "local" else "local → global alias"
            fmt src origin

        // Global-only sources (not referenced in local)
        for src in globalSources |> List.filter (fun s -> not (Set.contains s.Name localNames)) do
            fmt src "global"

        if globalSources.IsEmpty && localSources.IsEmpty then
            printfn "No sources configured."

        0
```

### 5. `SourceTests.fs` — tests for `Source.list`

Reuse the existing `makeDeps` helper. Add tests covering:

- Returns 0 and prints "No sources configured." when both configs are empty
- Lists a local source with URL labelled `[local]`
- Lists a local source without URL (alias) labelled `[local → global alias]`
- Lists a global-only source labelled `[global]`
- Local sources appear before global-only sources in output
- Returns 1 on config read error

Tests capture stdout via `Console.SetOut` and assert on the output string.

---

## Verification

```bash
# Unit tests
dotnet test tests/Eru.Tests/

# Manual smoke test
eru source list
```

Expected output when sources are configured:
```
  eru-knowledge  https://github.com/example/knowledge.git [branch: main] [basepath: KNOWLEDGE]  [local]
  shared-docs    https://github.com/example/shared.git                                           [global]
```
