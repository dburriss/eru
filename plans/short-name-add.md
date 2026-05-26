# Plan: Short-name resolution for `eru add`

## Context

`eru add` currently requires a full remote path (e.g. `knowledge/github-cli.md`). If a source has `BasePath = Some "knowledge"`, users should be able to type just `eru add github-cli` and have the tool infer `knowledge/github-cli.md` — because the file lives under the source's known base prefix and is a Markdown file. This removes the need to know or type the base path and extension for the common case.

## Behaviour

The expansion trigger is whether the input is a **bare filename** (no `/`). A bare filename is always resolved relative to the source's `BasePath`.

- **No `/` in path** (bare name) + source has `BasePath` → prepend `{basePath}/`
- **No extension** (no `.`) → append `.md`
- **Has `/`** → treat as an explicit remote path; only append `.md` if no extension

If the path already starts with `{basePath}/` it is not double-prefixed.

Examples (source has `BasePath = "knowledge"`):
| Input | Resolved remote path |
|---|---|
| `github-cli` | `knowledge/github-cli.md` |
| `github-cli.md` | `knowledge/github-cli.md` |
| `knowledge/github-cli` | `knowledge/github-cli.md` (no double-prefix) |
| `knowledge/github-cli.md` | `knowledge/github-cli.md` (unchanged) |
| `tools/adr` | `tools/adr.md` (explicit sub-path, ext added) |

Source discriminator prefix still works: `eru add orcai:github-cli` resolves as above using the `orcai` source.

## Implementation

### 1. New private function in `src/Eru.Domain/Add.fs`

Add after `deriveLocalPath` (around line 32):

```fsharp
let private resolveRemotePath (source: SourceConfig) (remotePath: string) : string =
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
    if withPrefix.Contains('.') then withPrefix
    else withPrefix + ".md"
```

### 2. Call site in `Add.run` (catch-all branch, `src/Eru.Domain/Add.fs` ~line 197)

Replace the current:
```fsharp
srcName
|> Result.bind (fun sn ->
    pullOne deps eff.Sources cmd.Target cmd.DryRun sn remotePath
    |> Result.map List.singleton)
```

With:
```fsharp
srcName
|> Result.bind (fun sn ->
    findSource eff.Sources sn
    |> Result.bind (fun source ->
        let expandedPath = resolveRemotePath source remotePath
        pullOne deps eff.Sources cmd.Target cmd.DryRun sn expandedPath
        |> Result.map List.singleton))
```

`findSource` is already a private function in `Add.fs` (line 34). `pullOne` will call it again internally — acceptable duplication to avoid changing `pullOne`'s signature.

## Tests

Add to `tests/Eru.Tests/AddTests.fs`, in the "Direct path pull" section. Reuse the existing `makeDeps`, `newState`, `makeSource`, `makeLocal`, `makeGlobal` helpers.

| Test name | Setup | Assertion |
|---|---|---|
| `bare name with BasePath expands to basePath prefix and md extension` | source `BasePath = Some "knowledge"`, `RemotePath = Some "github-cli"` | local path `github-cli.md`; lock `RemotePath = "knowledge/github-cli.md"` |
| `bare name with extension and BasePath gets prefix but no double extension` | source `BasePath = Some "knowledge"`, `RemotePath = Some "github-cli.md"` | lock `RemotePath = "knowledge/github-cli.md"` |
| `bare name with BasePath already prefixed does not double-prefix` | source `BasePath = Some "knowledge"`, `RemotePath = Some "knowledge/github-cli"` | lock `RemotePath = "knowledge/github-cli.md"` |
| `bare name without BasePath appends md extension only` | source `BasePath = None`, `RemotePath = Some "github-cli"` | lock `RemotePath = "github-cli.md"` |
| `explicit sub-path without extension gets md appended` | source `BasePath = Some "knowledge"`, `RemotePath = Some "tools/adr"` | lock `RemotePath = "tools/adr.md"` (no prefix added since path has slash) |

## Files to modify

- `src/Eru.Domain/Add.fs` — add `resolveRemotePath`, update the catch-all branch in `Add.run`
- `tests/Eru.Tests/AddTests.fs` — add 5 test cases

## Verification

```bash
dotnet test
```
