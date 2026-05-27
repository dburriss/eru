---
status: done
---
# Plan: Glob Support in `CollectionFileRef.RemotePath`

## Context

Collections currently require every file to be listed individually as an explicit `RemotePath`. Users want to specify glob patterns like `dotnet/**/*.md` and have all matching files pulled automatically. Git's sparse-checkout `--no-cone` mode already accepts gitignore-style glob patterns and materialises matching files into the working tree — so the git layer needs almost no change. The bulk of the work is widening the data flow to carry multiple `(resolvedPath, content)` pairs where a single `RemotePath` pattern used to produce one.

**No new NuGet dependency is needed.** Git does the glob matching; we enumerate what it materialises.

Gitignore-style patterns supported (same rules git sparse-checkout uses):
- `*` — any characters within a single path segment (e.g. `docs/*.md`)
- `**` — any number of path segments — **must be a standalone segment** (e.g. `**/*.md`, NOT `**.md`)
- `?` — single character
- A pattern with no slash (e.g. `*.md`) already matches recursively at all directory levels in gitignore semantics, so `*.md` and `**/*.md` are equivalent for depth.

> **Note:** `**.md` is non-standard — `**` must be surrounded by path separators. Use `**/*.md` for recursive matching.

---

## Changes

### 1. `src/Eru.Domain/Deps.fs` — widen the return type

```fsharp
// Before
FetchRemoteContent : string -> string -> string -> Result<string, string>

// After
FetchRemoteContent : string -> string -> string -> Result<(string * string) list, string>
//                                                         resolvedPath   content
```

### 2. `src/Eru.Adapters/GitAdapter.fs` — enumerate materialised files instead of single `File.Exists`

`fetchRemoteContent` keeps the same two git commands (clone + sparse-checkout). The `git sparse-checkout set --no-cone <pattern>` call already handles globs. Replace the `File.Exists` + `File.ReadAllText` block with:

```fsharp
let files =
    Directory.EnumerateFiles(tmpDir, "*", SearchOption.AllDirectories)
    |> Seq.filter (fun f ->
        let rel = Path.GetRelativePath(tmpDir, f)
        not (rel.StartsWith(".git")))
    |> Seq.map (fun f ->
        let rel = Path.GetRelativePath(tmpDir, f).Replace(Path.DirectorySeparatorChar, '/')
        rel, File.ReadAllText f)
    |> Seq.toList
if files.IsEmpty then
    Error $"'{remotePath}' not found in '{url}' on branch '{branch}'"
else
    Ok files
```

Return type becomes `Result<(string * string) list, string>`. The rest of the function (`withTempDir`, `runGit`) is unchanged.

`AdapterDeps.fs` needs no textual change — the compiler resolves the wiring automatically from the updated type.

### 3. `src/Eru.Domain/Add.fs` — three targeted changes

**a. `resolveRemotePath` — skip `.md` auto-append for glob patterns**

```fsharp
let private resolveRemotePath (source: SourceConfig) (remotePath: string) : string =
    let isGlob = remotePath.Contains('*') || remotePath.Contains('?')
    let isBare = not (remotePath.Contains('/'))
    let withPrefix =
        if isBare then
            match source.BasePath with
            | None -> remotePath
            | Some bp ->
                let prefix = if bp.EndsWith('/') then bp else bp + "/"
                if remotePath.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) then remotePath
                else prefix + remotePath
        else remotePath
    if isGlob || withPrefix.Contains('.') then withPrefix   // ← added `isGlob ||`
    else withPrefix + ".md"
```

Bare globs (e.g. `*.md`) still get the `basePath` prefix (same as bare exact names). Only the final `.md` auto-append is guarded.

**b. `pullOne` — return `Result<LockEntry list, string>`**

Change signature and produce one `LockEntry` per file returned by `FetchRemoteContent`. The `resolvedPath` from the tuple becomes `RemotePath` in the lock entry (not the original pattern):

```fsharp
let private pullOne
    (deps: Deps)
    (sources: SourceConfig list)
    (target: string option)
    (dryRun: bool)
    (sourceName: string)
    (remotePath: string) : Result<LockEntry list, string> =
    findSource sources sourceName
    |> Result.bind (fun source ->
        match source.Url with
        | None -> Error $"source '{sourceName}' has no URL"
        | Some url ->
            let branch = source.Branch |> Option.defaultValue "HEAD"
            deps.FetchRemoteContent url branch remotePath
            |> Result.bind (fun files ->
                files
                |> List.fold (fun acc (resolvedPath, content) ->
                    acc |> Result.bind (fun entries ->
                        let localPath = deriveLocalPath source.BasePath target resolvedPath
                        let hash = deps.HashContent content
                        if dryRun then
                            Ok (entries @ [{ LocalPath = localPath; SourceName = sourceName; RemotePath = resolvedPath; ContentHash = hash }])
                        else
                            deps.WriteLocalFile localPath content
                            |> Result.map (fun () ->
                                entries @ [{ LocalPath = localPath; SourceName = sourceName; RemotePath = resolvedPath; ContentHash = hash }])))
                    (Ok [])))
```

**c. `pullMany` — flatten lists from `pullOne`**

Change `Result.map (fun e -> entries @ [e])` → `Result.map (fun newEntries -> entries @ newEntries)`.

**d. `Add.run` call sites — drop `|> Result.map List.singleton`**

Lines 194–195 and 216–217 currently wrap `pullOne` in `List.singleton` to match the `Result<LockEntry list, string>` that `pullMany` returns. Since `pullOne` now returns a list directly, remove those wrappers.

### 4. `src/Eru.Domain/Sync.fs` — unwrap the list in `classifyEntry`

Lock entries store resolved exact paths (written by the add flow), so `FetchRemoteContent` called from sync always returns 0 or 1 files. Match accordingly:

```fsharp
match deps.FetchRemoteContent url branch entry.RemotePath with
| Error _ -> Missing entry
| Ok [] -> Missing entry
| Ok ((_, content) :: _) ->
    let hash = deps.HashContent content
    if hash = entry.ContentHash then Current entry
    else Drifted (entry, content)
```

---

## Test Changes

### `tests/Eru.Tests/AddTests.fs`

Update the `FetchRemoteContent` stub in `makeDeps` from:
```fsharp
FetchRemoteContent = fun _ _ path -> Ok $"content:{path}"
```
to:
```fsharp
FetchRemoteContent = fun _ _ path -> Ok [(path, $"content:{path}")]
```

All existing `Add` tests continue to exercise the same behaviour (single file per pattern). Add new tests:
- A `FetchRemoteContent` stub that returns multiple files for a glob pattern verifies that `pullOne` produces multiple `LockEntry` values and that all files are written.
- A test where `resolveRemotePath` receives `dotnet/*.md` verifies the `.md` suffix is NOT appended.

### `tests/Eru.Tests/SyncTests.fs`

Update `FetchRemoteContent` stubs in the same way (singleton list instead of bare string).

### `tests/Eru.Tests/GitAdapterTests.fs`

- Update existing tests: `fetchRemoteContent` now returns `Result<(string * string) list, string>`. Unwrap with `|> Result.map List.exactlyOne` or pattern match on the head.
- Add a new integration test: create a repo with multiple `.md` files under a subdirectory, call `fetchRemoteContent` with a glob pattern (e.g. `docs/*.md`), assert that the returned list contains all matching files.

---

## Verification

```bash
# Unit tests
dotnet test

# Manual smoke test — single exact path (regression check)
dotnet run --project src/Eru -- add <source>:<exact-file-path>

# Manual smoke test — glob pattern
dotnet run --project src/Eru -- add "<source>:dotnet/**/*.md"

# Collection with glob in eru.json global config
# Add a collection entry with "remotePath": "dotnet/*.md" and run:
dotnet run --project src/Eru -- add --collection my-collection
```
