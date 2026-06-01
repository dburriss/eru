---
status: done
---
# Plan: Structured return data from `search_knowledge`

## Context

The `search_knowledge` MCP tool currently returns a hand-formatted string that buries structured data (path, source, tags, description, excerpts) in human-readable text. The MCP 2025-11-25 spec added `structuredContent` on `CallToolResult` and `outputSchema` on tool definitions. The .NET SDK (v1.3.0, already installed) supports this via `McpServerToolAttribute.UseStructuredContent`, `OutputSchemaType`, and returning `CallToolResult` directly. Adding structured output allows LLM clients to parse results reliably while the text fallback keeps older clients working.

## Changes

### 1. `src/Eru.Mcp/SearchTypes.fs` — add `SearchHit` type

Add a new record after the existing types. This file is compiled first so it's available to `McpTools.fs`.

```fsharp
type SearchHit = {
    Path        : string
    Source      : string        // "cache" | "lock" | "local"
    SourceName  : string option
    Tags        : string list
    Description : string option
    Excerpts    : string list
}
```

`Source` is a plain string (not the DU) so the JSON schema and serialization are simple for clients.

### 2. `src/Eru.Mcp/McpTools.fs` — update `Search` method

**Imports to add:**
```fsharp
open System.Text.Json
open ModelContextProtocol.Protocol
```

**Attribute change** on the `Search` method:
```fsharp
[<McpServerTool(Name = "search_knowledge", UseStructuredContent = true, OutputSchemaType = typeof<SearchHit[]>)>]
```

**Return type change:** `string` → `CallToolResult`

**After computing `hits`**, map to `SearchHit[]` and build the result:
```fsharp
let structuredHits =
    hits |> List.map (fun (f, excerpts) ->
        {   Path        = f.RelPath
            Source      = match f.Source with Cache -> "cache" | Lock -> "lock" | Local -> "local"
            SourceName  = f.SourceName
            Tags        = f.Tags
            Description = f.Description
            Excerpts    = excerpts }) |> List.toArray

let textOutput =
    // keep existing string rendering exactly as-is (the `results` list + join logic)

let json = JsonSerializer.SerializeToElement(structuredHits)
CallToolResult(
    Content           = [| TextContentBlock(Text = textOutput) |],
    StructuredContent = System.Nullable(json)
)
```

The "no results" case returns a `CallToolResult` with `Content = [| TextContentBlock(Text = "No matching artifacts found.") |]` and `StructuredContent = System.Nullable(JsonSerializer.SerializeToElement([||]))`.

## Files modified

- `src/Eru.Mcp/SearchTypes.fs` — add `SearchHit` record (4 lines)
- `src/Eru.Mcp/McpTools.fs` — update attribute, return type, and result construction

No fsproj reordering needed (`SearchTypes.fs` already precedes `McpTools.fs`). No test files — there are no existing MCP-layer tests.

## Verification

1. `dotnet build src/Eru.Mcp/` — must compile cleanly
2. Run `eru mcp` and call `tools/list` via an MCP client — `search_knowledge` should show an `outputSchema` with a `SearchHit[]` shape
3. Call `search_knowledge` with a query — response should have both `content[0].text` (existing formatted string) and `structuredContent` as a JSON array of objects with `path`, `source`, `sourceName`, `tags`, `description`, `excerpts` fields
4. Call with no matches — `structuredContent` should be an empty array `[]`
