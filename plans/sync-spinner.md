# Add Spinner for `eru sync`

## Context

`eru sync` fetches remote content via multiple sparse git clones — one per configured source (manifest fetch) and one per lock entry (content comparison). This can take several seconds with no feedback. Adding a Spectre.Console status spinner gives the user visible progress during the wait, for the default interactive `table` output format.

## Approach

Wrap `Sync.execute` in `AnsiConsole.Status().Start<T>(...)` inside `SyncCli.run`, but only for the `Table` format. `Text` and `Json` formats remain unchanged (both are suited to scripting/pipes).

Spectre.Console `0.*` is already a dependency of `Eru.Cli` and `open Spectre.Console` is already present in `SyncCli.fs` — no new imports needed.

## Change

**File: `src/Eru.Cli/SyncCli.fs`** — modify `run` only:

```fsharp
let run (deps: Eru.Deps) (cmd: Cmd) : int =
    let syncResult =
        match cmd.Format with
        | Table ->
            let status = AnsiConsole.Status()
            status.Spinner <- Spinner.Known.Dots
            status.Start<Result<Sync.SyncResult, string>>("Syncing knowledge...", fun _ ->
                Sync.execute deps cmd.Options)
        | _ -> Sync.execute deps cmd.Options
    match syncResult with
    | Error e -> renderError e; 1
    | Ok result ->
        match cmd.Format with
        | Text  -> renderText result
        | Json  -> renderJson result
        | Table -> renderTable result
        0
```

Key points:
- Spinner only activates for `Table` (the default interactive format)
- `status.Spinner <- Spinner.Known.Dots` must be set before `.Start` is called
- Explicit type param `<Result<Sync.SyncResult, string>>` resolves the generic vs void `Start` overload unambiguously
- `Text` and `Json` paths call `Sync.execute` directly, unchanged

No other files need changes.

## Verification

```bash
# Build to confirm no compile errors
dotnet build

# Run sync — spinner should appear during execution, then table output
dotnet run --project src/Eru -- sync

# Text format — no spinner, plain output as before
dotnet run --project src/Eru -- sync --output text

# JSON format — no spinner, clean JSON output
dotnet run --project src/Eru -- sync --output json
```
