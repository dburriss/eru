---
status: pending
---
# Plan: Frontmatter-derived description and tags

## Context

Eru knowledge files can declare `description` and `tags` in YAML frontmatter.
Currently these fields are set only via config (`manifest.json`, collection config).
We want the MCP search to automatically pick up frontmatter values from files
that are available on disk — configured description wins; tags are merged (union,
deduplicated).

**Scope — MCP search only.** The CLI `eru search` (`Search.fs`) is purely
config-metadata-driven and never reads file content — frontmatter enrichment
there is a separate future concern.

In the MCP path, all three candidate sources are file-based and can benefit:

1. Cached collection files — `~/.cache/eru/sources/<name>/...`
2. Lock-file entries — locally pulled files tracked in `eru.lock`
3. Local `knowledge/` dirs — on-disk files in the project

---

## 1. New module — `src/Eru.Domain/Frontmatter.fs`

Pure module, no new NuGet dependencies. Hand-rolled minimal parser.

```fsharp
namespace Eru
module Frontmatter =
    type Parsed = { Description: string option; Tags: string list }
    let empty = { Description = None; Tags = [] }
    let parse (content: string) : Parsed
```

Parsing rules:
- File must start with `---` on line 1; anything else → `empty`
- Reads lines until the next `---` (closing delimiter)
- `description: some text` — single-line string; strips surrounding `"` or `'`
- `tags: [a, b, c]` — inline YAML list
- Block-list:
  ```
  tags:
    - a
    - b
  ```

Add to `Eru.Domain.fsproj` before files that use it.

---

## 2. Enrich candidates in `src/Eru.Mcp/McpTools.fs`

Add a local helper inside `KnowledgeTools`:

```fsharp
let readFrontmatter absPath =
    try Frontmatter.parse (File.ReadAllText absPath)
    with _ -> Frontmatter.empty
```

Merge rules applied at each site:
- **Description**: `configuredDesc |> Option.orElse fm.Description`
- **Tags**: `(configuredTags @ fm.Tags) |> List.distinct`

### Site 1 — Cached collection files (~line 57)

`meta` already provides configured tags/description. After computing the `file`
path, read frontmatter and merge in.

### Site 2 — Lock-file entries (~line 74)

Currently `Tags = []`, `Description = Some remotePath` (used as the display
label). Enrich tags from frontmatter; keep the remote-path fallback description
only if frontmatter supplies none.

### Site 3 — Local knowledge dirs (~line 91)

Currently `Tags = []`, `Description = None`. Enrich from frontmatter.

---

## 3. Tests — `tests/Eru.Tests/FrontmatterTests.fs`

| Scenario | Expected |
|---|---|
| Content with no `---` block | `empty` |
| Block present, no recognised fields | `empty` |
| `description` only | `Description = Some ...`, `Tags = []` |
| `tags` inline `[a, b]` | `Tags = ["a"; "b"]` |
| `tags` block-list | `Tags = ["a"; "b"]` |
| Both fields | both populated |
| Configured description wins | `Option.orElse` semantics |
| Tags merge | union with dedup |

Add `FrontmatterTests.fs` to `Eru.Tests.fsproj`.

---

## Verification

1. `dotnet build` — no errors.
2. `dotnet test` — all tests pass.
3. Create a file in `knowledge/` with:
   ```
   ---
   description: My test file
   tags: [foo, bar]
   ---
   ```
   Run `search_knowledge` with `tags: "foo"` — file appears with correct
   description and tags in the result label.
4. Configure the same file in a collection with `tags: ["configured"]`.
   Confirm result shows `tags: configured, foo, bar` (merged) and if a
   configured description exists it wins.
