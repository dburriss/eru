---
status: todo
---
# Plan: source-level description in manifest

## Compatibility

- **`SourceManifest` JSON** — additive. `Description` is `string option` so existing manifest files without the field deserialise cleanly to `None`. No migration needed.
- **`SiteSource` F# record** — adding a field is a compile-time break at the single construction site in `IndexBuilder.fs`. Caught immediately by the compiler; no silent runtime risk.

---

## Context

`SourceManifest` currently carries only `Version` and `Files`. There is no way
for a source repo to describe itself — consumers see a name and a URL but no
human-readable summary. This plan adds an optional top-level `Description` to
`SourceManifest` and threads it through the site generator so it appears on
source cards.

---

## 1. Domain type — `src/Eru.Domain/Config.fs`

Add `Description` to `SourceManifest`:

```fsharp
type SourceManifest = {
    Version     : int
    Description : string option   // NEW — human-readable summary of the source
    Files       : ManifestFileRef list
}
```

---

## 2. Adapter normalisation — `src/Eru.Adapters/ManifestAdapter.fs`

The `normalize` function that guards against null `Tags`/`Files` lists needs no
change — `Description` is already `string option` so a missing JSON key
deserialises cleanly to `None`.

---

## 3. Site model — `src/Eru.Site/SiteModel.fs`

Add `Description` to `SiteSource`:

```fsharp
type SiteSource = {
    Name        : string
    Url         : string option
    Description : string option   // NEW
    HasManifest : bool
    FileCount   : int
    Files       : SiteDocument list
}
```

---

## 4. Index builder — `src/Eru.Site/IndexBuilder.fs`

Read the cached manifest description when building `SiteSource`:

```fsharp
let manifestDescription =
    match deps.ReadCachedManifest src.Name with
    | Ok (Some m) -> m.Description
    | _           -> None

Some {
    Name        = src.Name
    Url         = src.Url
    Description = manifestDescription   // NEW
    HasManifest = manifestDescription.IsSome || hasManifest
    FileCount   = docs.Length
    Files       = docs
}
```

`hasManifest` can be derived from the same `ReadCachedManifest` call, removing
the duplicate call that currently exists.

---

## 5. HTML template — `src/Eru.Site/HtmlTemplates.fs`

Render the description in `sourceCard` inside `sourcesPage`, between the header
and the URL:

```fsharp
let descHtml =
    match s.Description with
    | Some d -> $"""<p class="source-card-desc">{escapeHtml d}</p>"""
    | None   -> ""
```

Add to the card body between `.source-card-header` and `.source-card-url`.

---

## 6. CSS — `src/Eru.Site/SiteGenerator.fs`

Add to the source card block:

```css
.source-card-desc {
  font-size: 0.85rem;
  line-height: 1.5;
  opacity: 0.85;
  margin: 0;
}
```

---

## Verification

```bash
dotnet build
dotnet test

# Manually add description to a test manifest:
# { "version": 1, "description": "Shared architecture decision records", "files": [...] }
# Cache it and run site generation, confirm description appears on the source card.
```
