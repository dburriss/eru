# Plan: Extend `eru init` with optional path and `--global` flag

## Context

`eru init` currently creates `eru.json` only in the current working directory. Users need to:
1. Init a config in an arbitrary directory without `cd`-ing first (`eru init /some/path`)
2. Bootstrap the global config file (`eru init --global`) at `~/.config/eru/config.json`

## Approach

Three source files change and one new test file is added. No new `Deps` slots are needed — `WriteGlobalConfig`, `ReadGlobalConfig`, `WriteLocalFile`, and `GetCwd` are already present.

---

## Changes

### 1. `src/Eru.Domain/Init.fs`

Update `Command` type:
```fsharp
type Command = { Force: bool; Path: string option; IsGlobal: bool }
```

Update `run`:
- If both `IsGlobal` and `Path` are set → print error, return 1.
- **Global branch** (`IsGlobal = true`):
  - If not `Force`, call `deps.ReadGlobalConfig()` — `Ok (Some _)` means file exists → error.
  - Write via `deps.WriteGlobalConfig { Version = 1; DefaultSources = []; Collections = []; Defaults = None }`.
  - Success message: `"Initialized global eru config."`.
- **Local branch**:
  - Resolve dir: `cmd.Path |> Option.defaultValue (deps.GetCwd())`.
  - Build path: `Path.Combine(dir, "eru.json")`.
  - Existence + force check with `System.IO.File.Exists` (same as current).
  - Write via `deps.WriteLocalFile configPath scaffold` (unchanged).
  - Success message: `"Initialized eru.json in <dir>"`.

### 2. `src/Eru.Cli/Args.fs`

Add two cases to `InitArgs`:
```fsharp
type InitArgs =
    | [<Unique>]              Force
    | [<Unique>]              Global
    | [<Unique; MainCommand>] Path of dir: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Force  -> "Overwrite existing eru.json."
            | Global -> "Create the global config (~/.config/eru/config.json)."
            | Path _ -> "Directory in which to create the config (default: current directory)."
```

`Path` is a positional optional arg so `eru init /some/dir` works naturally.

### 3. `src/Eru.Cli/CommandMapper/CommandMapper.fs`

Update `(|InitCmd|_|)`:
```fsharp
let (|InitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Init args ->
            Some {
                Init.Command.Force    = args.Contains InitArgs.Force
                Init.Command.IsGlobal = args.Contains InitArgs.Global
                Init.Command.Path     = args.TryGetResult InitArgs.Path
            }
        | _ -> None)
```

### 4. `tests/Eru.Tests/InitTests.fs` (new file)

Follow the `makeDeps` closure pattern from `SourceTests.fs`. Track what was written via `ref` cells.

Key cases:
1. `init` baseline — writes `eru.json` in cwd via `WriteLocalFile`.
2. `init /custom/dir` — writes `eru.json` at the given path.
3. `init --global` (no existing global) — calls `WriteGlobalConfig` with empty config, exit 0.
4. `init --global` (global exists, no `--force`) — exit 1, `WriteGlobalConfig` not called.
5. `init --global --force` (global exists) — overwrites, exit 0.
6. `init --global /some/path` — exit 1 (mutually exclusive).

Add `<Compile Include="InitTests.fs" />` to `tests/Eru.Tests/Eru.Tests.fsproj` before `GitAdapterTests.fs`.

---

## Verification

```sh
dotnet test
dotnet run --project src/Eru.Cli -- init /tmp/test-eru
cat /tmp/test-eru/eru.json
dotnet run --project src/Eru.Cli -- init --global --force
cat ~/.config/eru/config.json
```
