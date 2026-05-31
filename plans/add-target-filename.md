# Plan: `eru add --target` support for full file paths

## Context

`eru add` already has a `--target` / `-d` flag, wired through `Add.Command.Target` and consumed by `deriveLocalPath`. Today it unconditionally treats the value as a directory prefix (appending `/` if absent) and prepends it to the stripped remote filename.

The request is to make `--target` smarter using a simple trailing-slash rule:
- **Trailing `/` or `\`** → directory mode: prepend to stripped filename (e.g. `docs/` → `docs/adr.md`)
- **No trailing slash** → file-path mode: use the target string directly as `LocalPath` (e.g. `docs/my-adr.md` → `docs/my-adr.md`; `tools/mybinary` → `tools/mybinary`)
- **Omitted** → unchanged (use stripped remote path)

This is a **breaking change** for users who relied on `--target docs` (no slash) producing `docs/<filename>` — they now need `--target docs/`. The existing tests for this case must be updated accordingly.

---

## Files to change

| File | What changes |
|---|---|
| `src/Eru.Domain/Add.fs` | `deriveLocalPath` — trailing-slash detection |
| `src/Eru.Cli/Args.fs` | `Target _` usage string — reflect new semantics |
| `tests/Eru.Tests/AddTests.fs` | Update two existing tests; add two new tests |

---

## Implementation

### 1. `src/Eru.Domain/Add.fs` — `deriveLocalPath` (lines 32–36)

Replace the `Some t` branch:

```fsharp
    match target with
    | None   -> stripped
    | Some t ->
        if t.EndsWith('/') || t.EndsWith('\\') then
            t + stripped    // directory — preserve original filename
        else
            t               // full file path — use directly as LocalPath
```

### 2. `src/Eru.Cli/Args.fs` — usage string (line 34)

```fsharp
| Target _ -> "Local path for the pulled file; append a trailing / to treat as a directory (keeps original filename)."
```

### 3. `tests/Eru.Tests/AddTests.fs`

**Update** the existing `"target prefix is prepended to localPath"` test — `Target = Some "docs"` now means file-path `"docs"`, so change `Target` to `Some "docs/"` and keep the assertion `"docs/shared/adr.md"`.

**Update** the `"BasePath strip and target prefix are both applied"` test — same fix: `Some "docs"` → `Some "docs/"`.

**Add** two new tests:
- **Target is a full file path** — `Target = Some "docs/custom.md"` → local path is `"docs/custom.md"` (also assert `WrittenLock[0].LocalPath`)
- **Target is a file path without extension** (binary case) — `Target = Some "tools/mybinary"` → local path is `"tools/mybinary"`

---

## Verification

```bash
dotnet test tests/Eru.Tests/Eru.Tests.fsproj
```

All tests must pass.
