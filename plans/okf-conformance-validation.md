# OKF Conformance Validation Command (`eru okf validate <path>`)

## Context

eru is a tool for producing/consuming knowledge bundles in the Open Knowledge
Format (OKF v0.2). §11 of the spec defines what makes a bundle *conformant*,
and deliberately keeps that bar low — a parseable `type` field per concept,
and structural rules for the two reserved filenames (`index.md`, `log.md`).
Producers currently have no way to check their bundle meets this bar before
sharing it. This command gives them exactly that: a report of real
conformance violations (never style opinions, unknown types, unknown keys,
broken links, or missing optional fields — §11 explicitly forbids rejecting
on those). It's modeled directly on the existing `eru manifest verify`
command (`ManifestVerify.fs` / `ManifestVerifyCli.fs`) and changes no
existing behavior.

## Naming: also rename `manifest verify` → `manifest validate`

Per user decision, "validate" becomes eru's consistent verb for
conformance-style checks, so the existing `eru manifest verify` command is
renamed to `eru manifest validate` alongside adding the new `eru okf
validate`. `verify` is kept working as a backward-compatible CLI alias (not
a breaking change) via Argu's `AltCommandLine` attribute on the subcommand
case itself:

```fsharp
[<CliPrefix(CliPrefix.None)>]
type ManifestArgs =
    | [<SubCommand>] Init     of ParseResults<ManifestInitArgs>
    | [<SubCommand>] Add      of ParseResults<ManifestAddArgs>
    | [<SubCommand>] Remove   of ParseResults<ManifestRemoveArgs>
    | [<SubCommand; AltCommandLine("verify")>] Validate of ParseResults<ManifestValidateArgs>
```

No existing case in `Args.fs` currently combines `SubCommand` with
`AltCommandLine`, so verify during implementation (build + manual
`eru manifest verify` invocation) that Argu accepts and resolves the alias
as expected; it's a standard, independently-documented Argu feature so this
is a low-risk check, not a design fallback.

Rename scope (mechanical, no behavior change beyond the alias):
- `src/Eru.Domain/ManifestVerify.fs` → `ManifestValidate.fs`: module
  `ManifestVerify` → `ManifestValidate`, `VerifyResult` → `ValidateResult`.
  `execute` body unchanged.
- `src/Eru.Cli/ManifestVerifyCli.fs` → `ManifestValidateCli.fs`: active
  pattern `(|ManifestVerifyCmd|_|)` → `(|ManifestValidateCmd|_|)` (matching
  on `ManifestArgs.Validate` instead of `.Verify`), renderers/`run` updated
  to the new type names.
- `Args.fs`: `ManifestVerifyArgs` → `ManifestValidateArgs`; `ManifestArgs`
  case `Verify` → `Validate` (with the alias above); `Usage` text updated.
- `Program.fs`: `open Eru.Cli.ManifestVerifyCli` → `ManifestValidateCli`;
  dispatch arm `ManifestVerifyCmd cmd -> ManifestVerifyCli.run deps cmd` →
  `ManifestValidateCmd cmd -> ManifestValidateCli.run deps cmd`.
- `Eru.Domain.fsproj` / `Eru.Cli.fsproj`: update the two `<Compile>` file
  name entries in place (same position in the list).
- `tests/Eru.Tests/ManifestTests.fs`: update any `ManifestVerify.*`
  references to `ManifestValidate.*`.
- Any docs/README/help text mentioning `manifest verify` as the primary
  spelling should show `manifest validate` (mention `verify` still works as
  an alias where such docs exist), found via `grep -rn "manifest verify" --include="*.md" --include="*.fs"`.

## OKF §11 rules being enforced

1. Every non-reserved `.md` file has a parseable YAML frontmatter block.
2. That frontmatter block has a non-empty `type` field.
3. `index.md`/`log.md`, where present, follow §8/§9:
   - `index.md`: no frontmatter, **except** a bundle-root `index.md` MAY have
     an `okf_version` key (and nothing else) per §12.
   - `log.md`: date headings (`## YYYY-MM-DD`) must be ISO 8601
     `YYYY-MM-DD`.
4. A bare `verified: { by, at }` mapping is a valid one-element list, not a
   violation (§5.2) — reuse `Frontmatter.verified`, which already does this
   (`Frontmatter.fs:72-76`).
5. Must NOT flag: missing optional fields, unknown `type` values, unknown
   extra frontmatter keys, broken cross-links, missing `index.md`.

## Design

### Domain: `src/Eru.Domain/Frontmatter.fs` (small addition)

`Frontmatter.parse` currently collapses "no block", "malformed YAML", and
"valid but empty" into one `empty` result — too coarse for a validator that
must distinguish rule 1 from rule 2 violations. Add a diagnostic variant
alongside the existing `parse`, without changing `parse`'s signature or any
caller:

```fsharp
type ParseOutcome =
    | NoBlock
    | MalformedYaml of string
    | Parsed of FrontmatterMap

let tryParse (parseYaml: Yaml.Parse) (content: string) : ParseOutcome =
    match extractBlock content with
    | None -> NoBlock
    | Some blockText ->
        match parseYaml blockText with
        | Ok (Yaml.Map kvs) -> Parsed (Map.ofList kvs)
        | Ok _ -> MalformedYaml "frontmatter block is not a YAML mapping"
        | Error e -> MalformedYaml e
```

(Make `extractBlock` internal/reused as-is — no change needed there.)

### Domain: `src/Eru.Domain/OkfValidate.fs` (new file, after `Sync.fs` in
`Eru.Domain.fsproj`)

```fsharp
namespace Eru

module OkfValidate =
    type Command = { Path: string }

    type Violation = { Path: string; Rule: string; Message: string }

    type ValidateResult = { TotalConcepts: int; Violations: Violation list }

    let execute (deps: Deps) (cmd: Command) : Result<ValidateResult, string> =
        ...
```

Logic:
- `deps.ListMarkdownFiles cmd.Path` → relative paths of all `*.md` files
  under `cmd.Path` (new `Deps` field, see below).
- For each relative path, `deps.ReadLocalFile (Path.Combine(cmd.Path, rel))`.
- Classify by `Path.GetFileName`:
  - `"index.md"` → structural check: run `Frontmatter.tryParse`; if it
    yields `Parsed` with any key other than `okf_version`, or `Parsed` with
    `okf_version` while `rel` is not the bundle-root index (i.e.
    `Path.GetDirectoryName rel <> ""`), that's a violation. `NoBlock` is
    fine (no frontmatter is the norm). `MalformedYaml` is only a violation
    if a block exists at all — reuse `extractBlock`-backed detection (a
    `NoBlock` outcome already covers "no `---` fence", so no separate check
    needed).
  - `"log.md"` → skip frontmatter checks; scan lines matching `^##\s+(.+)`,
    validate each heading text against `^\d{4}-\d{2}-\d{2}$` (regex), one
    violation per non-conforming heading.
  - anything else (concept file) → `Frontmatter.tryParse`:
    - `NoBlock` → violation, rule "no-frontmatter".
    - `MalformedYaml msg` → violation, rule "malformed-yaml", message = msg.
    - `Parsed fm` → if `Frontmatter.type_ fm` is `None`, violation rule
      "missing-type"; else conformant, count towards `TotalConcepts`.
- No handling needed for `verified`/`generated` tolerance beyond what
  `Frontmatter` lenses already guarantee — the validator never inspects
  those fields itself, so §11's "MUST NOT reject on missing optional
  families" falls out for free.

### `Deps` addition: one new field

```fsharp
ListMarkdownFiles : string -> Result<string list, string>
```

Adapter impl (`src/Eru.Adapters/`, e.g. new small function in
`ManifestAdapter.fs` or a new `OkfAdapter.fs`), following the existing walk
pattern in `GitAdapter.fs:36-53`:

```fsharp
let listMarkdownFiles (root: string) : Result<string list, string> =
    try
        Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
        |> Seq.filter (fun f ->
            let rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/')
            not (rel.Split('/') |> Array.exists (fun seg -> seg = ".git")))
        |> Seq.map (fun f -> Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
        |> Seq.sort |> Seq.toList |> Ok
    with ex -> Error ex.Message
```

Wire into `AdapterDeps.fs` as `ListMarkdownFiles = OkfAdapter.listMarkdownFiles` (no `cwd` closure needed — takes the path argument directly, unlike `ResolveLocalGlob`).

This adds one field to the `Deps` record, so all 11 existing full-literal
construction sites need a stub added (`ListMarkdownFiles = fun _ -> Ok []` in
the 10 test files, real impl in `AdapterDeps.fs`):
`DisconnectTests.fs`, `AddTests.fs`, `IndexBuilderTests.fs`, `InitTests.fs`,
`CollectionTests.fs`, `RemoveTests.fs`, `SyncTests.fs`, `SearchTests.fs`,
`ManifestTests.fs`, `SourceTests.fs`.

`ReadLocalFile` (`AdapterDeps.fs:60-64`) already takes arbitrary absolute
paths with no cwd-joining, so it's reusable as-is for reading each matched
file's content.

### CLI: `src/Eru.Cli/Args.fs`

```fsharp
type OkfValidateArgs =
    | [<MainCommand; ExactlyOnce>] Path of path: string
    | [<Unique; AltCommandLine("-o")>] Output of format: string
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Path _   -> "Directory to validate against OKF §11."
            | Output _ -> "Output format: table (default), text, json."

[<CliPrefix(CliPrefix.None)>]
type OkfArgs =
    | [<SubCommand>] Validate of ParseResults<OkfValidateArgs>
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Validate _ -> "Validate a directory tree for OKF conformance (§11)."
```

Add `| [<SubCommand>] Okf of ParseResults<OkfArgs>` to `EruArgs` (alongside
`Site`, near line 348-379) with a matching `Usage` arm:
`"Validate a knowledge bundle for OKF conformance."`.

### CLI: `src/Eru.Cli/OkfValidateCli.fs` (new file, inserted right before
`Program.fs` in `Eru.Cli.fsproj`)

Mirrors `ManifestVerifyCli.fs` exactly:
- `type Cmd = { Path: string; Format: OutputFormat }`
- `(|OkfValidateCmd|_|)` active pattern pulling `EruArgs.Okf` →
  `OkfArgs.Validate` → `{ Path = ...; Format = parseFormat ... }`.
- `renderText` / `renderJson` / `renderTable`, printing violations as
  `✗ {path} — {message}` and a trailing `✓ {n} concepts conformant` line
  (matching the example output in the request), to stdout for the summary,
  `eprintfn` for individual violations (matching `ManifestVerifyCli`'s
  convention of pushing failures to stderr).
- `run (deps: Eru.Deps) (cmd: Cmd) : int` → `OkfValidate.execute deps { Path = cmd.Path }`,
  render, return `0` if `Violations.IsEmpty` else `1`.

### `Program.fs`

Add `open Eru.Cli.OkfValidateCli` near the other `Cli` opens, and
`| OkfValidateCmd cmd -> OkfValidateCli.run deps cmd` alongside
`ManifestVerifyCmd`/`SiteGenerateCmd`.

### Tests: `tests/Eru.Tests/OkfValidateTests.fs` (new file)

Follow `ManifestTests.fs`'s `makeDeps` pattern (full `Deps` literal +
override hooks for `ListMarkdownFiles` and `ReadLocalFile`). Cover:
- clean bundle → zero violations, correct `TotalConcepts`.
- concept `.md` with no frontmatter block → `no-frontmatter` violation.
- concept with frontmatter but empty/missing `type` → `missing-type`.
- concept with malformed YAML → `malformed-yaml`, message surfaced.
- bundle-root `index.md` with `okf_version` only → no violation.
- non-root `index.md` with any frontmatter → violation.
- `index.md` with frontmatter key other than `okf_version` → violation.
- `log.md` with a non-`YYYY-MM-DD` heading (e.g. `2026-5-22`) → violation.
- concept with bare `verified: { by, at }` mapping → no violation (proves
  §5.2 tolerance holds).
- unknown `type` value, unknown extra frontmatter key, broken cross-link →
  explicitly assert NO violation is raised for these (regression guard for
  §11's "must not reject" list).

## Verification

- `dotnet build` — confirm both new domain/CLI files compile and the
  `Deps` field addition doesn't break other adapters.
- `dotnet test` — existing suite still passes after the 10 test-file stub
  additions; new `OkfValidateTests.fs` cases pass.
- Manual: build a small temp bundle with one conformant concept, one
  missing `type`, a good `index.md`/root `index.md` with `okf_version`, and
  a `log.md` with a bad date heading; run
  `eru okf validate <path>` and confirm the printed report matches the
  violations described above and exits non-zero; run again against a fully
  conformant bundle and confirm exit code `0` and the `✓ N concepts
  conformant` line.
- Manual: run both `eru manifest validate` and `eru manifest verify` in a
  repo with a `.eru/manifest.json` and confirm both resolve to the same
  command and produce identical output (proving the alias works).
