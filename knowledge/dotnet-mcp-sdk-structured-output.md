---
description: How to return structured content from .NET MCP SDK tool methods using CallToolResult, StructuredContent, and OutputSchema
tags: [dotnet, mcp, sdk, structured-output, tools]
---

# .NET MCP SDK — Structured Tool Output

SDK: `ModelContextProtocol` NuGet package (v1.3.0+). Spec: MCP 2025-11-25.

## What it is

Tools can return two things simultaneously:

- **`content`** — unstructured `ContentBlock` list (text, images, etc.) — for backward-compatible clients
- **`structuredContent`** — a typed JSON object — for clients that understand structured output

The tool definition can also advertise an **`outputSchema`** (JSON Schema) so clients can validate and reason about the shape of `structuredContent`.

Spec rule: a tool returning `structuredContent` SHOULD also populate `content` with the serialized JSON as a text block.

## Attribute configuration

```fsharp
[<McpServerTool(
    Name = "my_tool",
    UseStructuredContent = true,        // advertises outputSchema
    OutputSchemaType = typeof<MyResult> // SDK generates schema from this type
)>]
```

`OutputSchemaType` requires `UseStructuredContent = true`. The SDK generates the JSON Schema from the given .NET type via `System.Text.Json`.

## Return type

Change the method return type from `string` to `CallToolResult`:

```fsharp
open System.Text.Json
open ModelContextProtocol.Protocol

member _.MyTool(...) : CallToolResult =
    let result : MyResult = { ... }
    let json = JsonSerializer.SerializeToElement(result)
    let text = JsonSerializer.Serialize(result)
    CallToolResult(
        Content     = [| TextContentBlock(Text = text) |],
        StructuredContent = json
    )
```

Other supported return types (auto-wrapped by SDK):

| Return type | How SDK handles it |
|---|---|
| `string` | Single `TextContentBlock` in `content`, no `structuredContent` |
| `ContentBlock` | Single-item `content` list |
| `IEnumerable<ContentBlock>` | `content` list as-is |
| `CallToolResult` | Returned directly — use this for structured output |
| Any other type | Serialized to JSON as a text content block |

## Error signalling

```fsharp
CallToolResult(
    Content = [| TextContentBlock(Text = "Something went wrong: ...") |],
    IsError = true
)
```

`IsError = true` is a tool-execution error (actionable by the LLM). Protocol errors (unknown tool, malformed request) are JSON-RPC errors and use a different path.

## Full example

```fsharp
type SearchHit = {
    Path        : string
    Source      : string   // "cache" | "lock" | "local"
    SourceName  : string option
    Tags        : string list
    Description : string option
    Excerpts    : string list
}

[<McpServerTool(Name = "search_knowledge", UseStructuredContent = true, OutputSchemaType = typeof<SearchHit[]>)>]
[<Description("Search knowledge artifacts.")>]
member _.Search(query: string) : CallToolResult =
    let hits : SearchHit[] = // ... build results ...
    let json = JsonSerializer.SerializeToElement(hits)
    let text = JsonSerializer.Serialize(hits)
    CallToolResult(
        Content           = [| TextContentBlock(Text = text) |],
        StructuredContent = json
    )
```

## Resources

Resources do **not** have `structuredContent` — that is tool-only. To return structured data from a resource, use `MimeType = "application/json"` and return a JSON string. The SDK auto-wraps `string` returns as `TextResourceContents`.

## Links

[MCP SDK API Documentation](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.html)