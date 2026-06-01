---
status: done
---
# Plan: Short hash addressing for `eru source files` and `eru add`

## Context

`eru source files <name>` lists the concrete files a source exposes. Long paths (e.g. `github/copilot-dotnet-environment.md`) are tedious to type when passing to `eru add`. This feature adds a short deterministic hash to each file in the listing so users can reference files by a few characters rather than the full path.

---

## Approach

### Hash function

Add a pure `pathShortHash (path: string) : string` helper in `Patterns.fs`. SHA256 of the UTF-8 path string, lower-hex, take first 8 chars. Deterministic: same path always gives same hash.

### `eru source files` output

Prepend the 8-char hash to each line:

```
Files for source: knowledge

  a3f29e1c  github/apps.md  [github]  — GitHub reference files
  c91b4d7a  github/cli.md  [github]  — GitHub reference files
  7e4a02fb  github/copilot-dotnet-environment.md  [github, copilot]  — How to configure...
```

### `eru add <source>:<hash-prefix>` resolution

In `pullOne` (`Add.fs`), after the source config is resolved and before calling `FetchRemoteContent`:

1. Detect short hash: remote path matches `^[0-9a-f]{3,8}$` (all hex, 3–8 chars, no `/` or `.`).
2. If detected: call `deps.ListRemoteFiles url branch source.BasePath` to get all paths.
3. Hash each path with `pathShortHash`, filter to those whose hash starts with the supplied prefix.
4. **0 matches** → `Error "no file found for hash prefix '<prefix>'"`.
5. **2+ matches** → `Error "ambiguous short hash '<prefix>' — be more specific"`.
6. **1 match** → replace `remotePath` with the resolved concrete path and continue normally.

No new `Deps` fields needed — `ListRemoteFiles` is already wired.

---

## Files to change

### 1. `src/Eru.Domain/Patterns.fs`

Add after the existing helpers:

```fsharp
let pathShortHash (path: string) : string =
    let bytes = System.Text.Encoding.UTF8.GetBytes path
    let hex   = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData bytes).ToLowerInvariant()
    hex.[..7]
```

### 2. `src/Eru.Domain/Source.fs`

In `files`, prepend `pathShortHash path` to each output line:

```fsharp
let hash = Patterns.pathShortHash path
printfn $"  {hash}  {path}{tagStr}{descStr}"
```

### 3. `src/Eru.Domain/Add.fs`

In `pullOne`, add a short-hash resolution step before `deps.FetchRemoteContent`:

```fsharp
let private isShortHash (s: string) =
    s.Length >= 3 && s.Length <= 8 && s |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))

let private resolveShortHash
    (deps: Deps) (source: SourceConfig) (prefix: string) : Result<string, string> =
    match source.Url with
    | None -> Error $"source '{source.Name}' has no URL"
    | Some url ->
        match deps.ListRemoteFiles url source.Branch source.BasePath with
        | Error e -> Error e
        | Ok paths ->
            let matches = paths |> List.filter (fun p -> (Patterns.pathShortHash p).StartsWith prefix)
            match matches with
            | []  -> Error $"no file found for hash prefix '{prefix}'"
            | [p] -> Ok p
            | _   -> Error $"ambiguous short hash '{prefix}' — {matches.Length} files match, be more specific"
```

In `pullOne`, before calling `resolveRemotePath`:

```fsharp
|> Result.bind (fun source ->
    let resolvedRemotePath =
        if isShortHash remotePath then resolveShortHash deps source remotePath
        else Ok remotePath
    resolvedRemotePath |> Result.bind (fun remotePath ->
        // existing FetchRemoteContent logic ...
    ))
```

---

## Verification

```bash
dotnet build

# Show hashes
dotnet run --project src/Eru -- source files knowledge

# Add by full hash
dotnet run --project src/Eru -- add knowledge:a3f29e1c

# Add by short prefix (unique)
dotnet run --project src/Eru -- add knowledge:a3f

# Ambiguous prefix (if applicable)
dotnet run --project src/Eru -- add knowledge:a

# Run tests
dotnet test
```
