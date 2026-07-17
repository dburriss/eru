---
status: done
---

# Plan: Detect and restore local drift in `eru sync`

## Context

`eru sync` currently only detects drift when the **remote** content has changed from the lock hash. If a user manually edits or deletes a local file that eru manages, and the remote hasn't changed, sync classifies it as `ECurrent` and silently ignores the local change. This means local files can quietly diverge from what was pulled.

eru is a one-way street: remote → local. The lock hash is the contract for what each local file should contain. Sync should enforce that contract in both directions — pulling remote updates *and* restoring locally drifted files.

---

## Goal

After every sync (non-dry-run), every local file tracked in the lock matches the content hash recorded in the lock. Remote updates are handled as before. The lock hash is never changed unless the remote has moved.

---

## Classification matrix

| Remote vs Lock | Local vs Lock       | State           | Action                                  |
|----------------|---------------------|-----------------|-----------------------------------------|
| Same           | Same                | `ECurrent`      | Nothing                                 |
| Same           | Different or missing| `ELocalDrifted` | Write remote content to local; lock unchanged |
| Different      | Any                 | `EDrifted`      | Write remote content to local; update lock hash |
| Not found      | Any                 | `EMissing`      | Nothing                                 |
| Blocked        | Any                 | `EBlocked`      | Nothing                                 |
| Source missing / no URL | Any      | `ESkipped`      | Nothing                                 |

---

## Changes

### 1. `src/Eru.Domain/Deps.fs`

Add `ReadLocalFile` to the `Deps` record:

```fsharp
ReadLocalFile : string -> Result<string option, string>
```

Returns `Ok None` if the file does not exist, `Ok (Some content)` if it does, `Error msg` on IO failure. Covers both the "missing" and "modified" cases in a single dep.

---

### 2. `src/Eru.Adapters/AdapterDeps.fs`

Wire the new dep in `AdapterDeps.create`:

```fsharp
ReadLocalFile = fun path ->
    try
        if File.Exists path then Ok (Some (File.ReadAllText path))
        else Ok None
    with ex -> Error ex.Message
```

---

### 3. `src/Eru.Domain/Sync.fs`

**Add `LocalDrifted` to the public `SyncStatus` DU:**

```fsharp
| LocalDrifted   // "would restore" on dry-run; "restored" on actual run
```

**Add `ELocalDrifted` to the private `EntryResult` DU:**

```fsharp
| ELocalDrifted of LockEntry * string   // entry + remote content to write
```

**Update `toSyncEntry`:**

```fsharp
| ELocalDrifted (e, _) -> { Status = LocalDrifted; LocalPath = e.LocalPath }
```

**Update classify logic** — after the remote hash check, add a local hash check for the `ECurrent` case:

```fsharp
let hash = deps.HashContent content
if hash <> entry.ContentHash then
    EDrifted (entry, content)
else
    let localHash =
        match deps.ReadLocalFile entry.LocalPath with
        | Ok (Some c) -> Some (deps.HashContent c)
        | _           -> None  // missing file or read error → treat as drifted
    match localHash with
    | Some h when h = entry.ContentHash -> ECurrent entry
    | _                                 -> ELocalDrifted (entry, content)
```

**Update write step** — `ELocalDrifted` entries are written to disk like `EDrifted` but excluded from the lock hash update:

```fsharp
let drifted      = classified |> List.choose (function EDrifted (e, c)      -> Some (e, c) | _ -> None)
let localDrifted = classified |> List.choose (function ELocalDrifted (e, c) -> Some (e, c) | _ -> None)
let toWrite      = drifted @ localDrifted

let writeError =
    toWrite |> List.tryPick (fun (entry, content) ->
        match deps.WriteLocalFile entry.LocalPath content with
        | Error e -> Some e
        | Ok ()   -> None)

// lock update: only drifted (remote changed), not localDrifted (lock hash already correct)
let updatedEntries =
    entries |> List.map (fun entry ->
        match drifted |> List.tryFind (fun (e, _) -> e.LocalPath = entry.LocalPath) with
        | Some (_, content) -> { entry with ContentHash = deps.HashContent content }
        | None              -> entry)
```

Early-exit when both `drifted` and `localDrifted` are empty (no writes needed) before attempting IO.

---

### 4. `src/Eru.Cli/SyncCli.fs`

**`statusLabel`** — add the new case:

```fsharp
| Sync.LocalDrifted -> if isDryRun then "local-drifted" else "restored"
```

**`counts`** — add `LocalDrifted` to the counts tuple and update all callers:

```fsharp
let nLocalDrifted = entries |> List.sumBy (fun e -> if e.Status = Sync.LocalDrifted then 1 else 0)
```

**Summary lines** — include `restored`/`local-drifted` count in dry-run and real-run output:

```
Sync complete: 2 updated, 1 restored, 3 current, 0 missing, 0 skipped, 0 blocked.
Sync dry-run:  2 drifted, 1 local-drifted, 3 current, 0 missing, 0 skipped, 0 blocked.
```

---

## Tests — `tests/Eru.Tests/SyncTests.fs`

Add three new test cases following the existing fake-deps pattern:

| # | Setup | Expected result |
|---|---|---|
| 1 | Remote == lock, local file modified (different content) | `ELocalDrifted`; file written with remote content; lock hash unchanged |
| 2 | Remote == lock, local file deleted/missing | `ELocalDrifted`; file written (recreated); lock hash unchanged |
| 3 | Remote == lock, local file untouched | `ECurrent`; nothing written; lock unchanged |

---

## Files to modify

| File | Change |
|---|---|
| `src/Eru.Domain/Deps.fs` | Add `ReadLocalFile` field |
| `src/Eru.Adapters/AdapterDeps.fs` | Wire `ReadLocalFile` |
| `src/Eru.Domain/Sync.fs` | New `ELocalDrifted` / `LocalDrifted` states; updated classify + write logic |
| `src/Eru.Cli/SyncCli.fs` | `statusLabel`, `counts`, and summary lines for `LocalDrifted` |
| `tests/Eru.Tests/SyncTests.fs` | 3 new test cases |
