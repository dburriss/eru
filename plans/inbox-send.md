# `eru inbox` — capture to configured knowledge inboxes

## Context

The [knowledge](../../knowledge/README.md) repo (an eru knowledge source) has an `inbox/` capture
point: `inbox/raw/<channel>/` holds unprocessed material (articles, snippets, transcripts, links);
a periodic `ingestor` agent (`.agents/agents/ingestor.md`) curates it into structured OKF notes and
archives the raw item. Today the only way to get something into `inbox/raw/` is to hand-write a
file there. `todo.md` already carries the line item "Send to local inbox" — this plan implements it
as a new `eru inbox` command group: configure one or more inboxes (each just a local filesystem
directory — **not** a configured eru `source`; an inbox is a write-target directory, unrelated to
eru's pull-only `Sources` concept), each with its own set of named channels, then `eru inbox send` a
message, local file, or URL into one.

Config is modeled as a **map keyed by inbox name**, each holding a **map of named channels** — not a
flat list — specifically so it's easy to (a) configure multiple knowledge directories as separate
inboxes, (b) give each its own set of channels, and (c) extend a channel's config later (e.g. which
agent processes it) without a breaking schema change.

**Scope for v1** (per discussion): an inbox is always a **local filesystem directory** — eru writes
directly into that working tree. No git commit/push is added; you (or the ingestor) commit normally.
This matches eru's existing read-only relationship with git remotes (`GitAdapter.fs` only clones into
throwaway temp dirs today — there is no commit/push code anywhere in the codebase) and avoids adding
network-auth-handling complexity for a personal, single-machine workflow. A remote (clone+push) inbox
kind is a natural v2 if ever needed — the `InboxConfig` shape below is deliberately just `{ path,
channels }` so that could later become a discriminated union of "local path" vs. "remote repo"
without disturbing the CLI.

**Primary usage is cross-repo**: the expected workflow is running `eru inbox send` while working in
some *other* project's codebase, sending a note/file/link off to the knowledge base for later
processing — not while sitting inside the knowledge repo itself. So `inbox add`/`inbox channel add`/
`inbox remove`/`inbox channel remove` need their existing `-g`/`--global` flag to actually be used day
to day (global config at `~/.config/eru/config.json`, via `GlobalConfig.DefaultInboxes`), and,
critically, **`inbox send` must work from a directory with no local `.eru/config.json` at all** —
unlike `Add.ensureSource`'s local-write path (`Add.fs:216-222`, which requires `eru init` first),
`inbox send` only *reads* the effective config (`deps.ReadGlobalConfig()`/`ReadLocalConfig()`, both
already tolerate a missing file — see `docs/reference/config-file.md`: "Both are optional"). It never
needs to write to the current repo's config, so no `eru init` requirement applies to it.

**Companion change needed outside this repo**: `ingestor.md` in the knowledge repo currently reads
`captured_at`/`original_url` only from a `.meta.json` sidecar. This plan has eru-authored markdown
carry that metadata as YAML frontmatter instead (sidecars stay reserved for external, non-markdown
files eru didn't author — see below). `ingestor.md` will need a small follow-up update to also read
frontmatter on raw `.md` items; flag this to the user after implementation, it's a separate repo/PR.

## Config shape

New types in `src/Eru.Domain/Domain.fs`, alongside `SourceConfig`/`CollectionConfig`:

```fsharp
// Open for extension: e.g. `Agent: string option` is unused today but is exactly the kind of
// per-channel field this shape exists to add later without breaking existing config files.
type InboxChannelConfig = {
    Description : string option
    Agent       : string option   // reserved: which agent processes this channel (e.g. "ingestor")
}

type InboxConfig = {
    Path           : string                           // local filesystem directory (e.g. a knowledge repo checkout)
    RawPath        : string option                     // path within Path to the raw capture folder; default "inbox/raw"
    DefaultChannel : string option                      // channel `-c` falls back to; default "default"
    Channels       : Map<string, InboxChannelConfig>    // channel name -> config; entries are optional —
                                                          // sending to an unlisted channel is still allowed,
                                                          // this map is only for channels needing extra config
}
```

`InboxConfig` is entirely self-contained — it does **not** reference eru's `SourceConfig`/`Sources` at
all. A "source" is a place eru *pulls from*; an inbox is a place eru *writes to*. They're unrelated
concepts that happen to often point at the same directory on disk, so `inbox add` takes a plain path,
not a source name — no `eru source add` step is required first.

- `LocalConfig` gains `Inboxes: Map<string, InboxConfig>`; `GlobalConfig` gains `DefaultInboxes: Map<string,
  InboxConfig>` — same naming relationship as `Sources`/`DefaultSources`.
- `LocalSettings`/`GlobalDefaults` gain `DefaultInbox: string option` — which inbox `-i` falls back to
  when omitted, used only when more than one inbox is configured (see resolution below).
- `Config.merge` (`src/Eru.Domain/Config.fs`): merge `Inboxes` by key the same way `Sources` are merged
  by `Name` (local entry wins for a given key; global entries not present locally are appended). No
  cross-reference validation is needed (unlike collections/sources) — `Path` is validated directly
  against the filesystem, at `inbox add` time and again defensively at `inbox send` time.
- `EffectiveConfig` gains `Inboxes: Map<string, InboxConfig>`.
- `Map<string, 'T>` needs no new JSON converter — F#'s `Map` implements `IReadOnlyDictionary<'K,'V>`,
  which `System.Text.Json` serializes as a plain JSON object natively; the existing `OptionConverterFactory`
  (`src/Eru.Adapters/Serialization.fs`) already handles the `option` fields inside it.
- Additive fields on existing types (`LocalConfig`/`GlobalConfig`/`LocalSettings`/`GlobalDefaults`) follow
  the same non-breaking-migration precedent as `SourceConfig.Bundles` replacing `BasePath`
  (`Config.fs:150-155,464-478`) — old config files simply deserialize with empty maps/`None`.
- `Init.fs`'s scaffold gains an empty `"inboxes": {}` (local) / `"defaultInboxes": {}` (global), matching
  how `sources`/`collections` are scaffolded empty today.

Resolving the inbox for `send` when `-i` is omitted: exactly one configured inbox → use it; else fall
back to `DefaultInbox` if set; else error listing the configured inbox names.

## CLI

New nested command group, `[<CliPrefix(CliPrefix.None)>] InboxArgs` in `EruArgs`
(`src/Eru.Cli/Args.fs`) — mirrors `SourceArgs`'s own nested `SourceBundleArgs`:

```
eru inbox add <name> <path> [--raw-path <path>] [--default-channel <channel>] [-g] [--dryrun] [-o <format>]
eru inbox list [-o <format>]
eru inbox remove <name> [-g] [--dryrun] [-o <format>]

eru inbox channel add <inbox> <channel> [--agent <agent>] [-d <description>] [-g] [--dryrun] [-o <format>]
eru inbox channel list <inbox> [-o <format>]
eru inbox channel remove <inbox> <channel> [-g] [--dryrun] [-o <format>]

eru inbox send <content> [-i <inbox>] [-c <channel>] [-t <title>] [-n <note>]
                         [--as message|file|url] [--dryrun] [-o <format>]
```

- `inbox add`/`remove` follow `SourceAddArgs`/`SourceRemoveArgs`'s flag conventions (`-g` global,
  `--dryrun`, `-o` output). `<name> <path>` is a tuple `MainCommand` (`Name_And_Path of name: string *
  path: string`, matching the `Source_And_Path` idiom below). `add` errors if `<path>` isn't an
  existing local directory (`Deps.DirectoryExists`) — it is never treated as a git URL or checked
  against configured sources.
- `inbox channel add/remove` take `Inbox_And_Channel of inbox: string * channel: string` as the
  MainCommand tuple, mirroring `SourceBundleAddArgs`'s `Source_And_Path` tuple positional exactly.
  `channel add` errors if `<inbox>` isn't configured.
- `inbox send`:
  - `<content>` (MainCommand, optional) — message text, a local file path, or a URL. If omitted and
    stdin is redirected, read the whole of stdin as the message.
  - `-i`/`--inbox` — which configured inbox to send into; auto-resolved per the rule above when omitted.
  - `-c`/`--channel` — channel within that inbox; default `InboxConfig.DefaultChannel` or `"default"`.
    Not required to be pre-registered via `inbox channel add` — that command is only for channels that
    need extra config (e.g. a future `--agent`).
  - `-t`/`--title` — explicit filename slug; otherwise derived (see below).
  - `-n`/`--note` — extra context text folded into the body of a `message`/`url` capture only.
  - `--as` — force content-type classification instead of auto-detecting.
  - `--dryrun` — show the resolved target path and classification without writing.

Domain files, one per command, mirroring `SourceAdd.fs`/`SourceList.fs`/`SourceRemove.fs`/
`SourceBundleAdd.fs` etc.: `src/Eru.Domain/InboxAdd.fs`, `InboxList.fs`, `InboxRemove.fs`,
`InboxChannelAdd.fs`, `InboxChannelList.fs`, `InboxChannelRemove.fs`, `InboxSend.fs`. Same one-per-command
`Eru.Cli/InboxAddCli.fs` etc. pattern for the Argu active-pattern + render + `run` wiring, and the same
`Program.fs` dispatch additions (`| InboxAddCmd cmd -> InboxAddCli.run deps cmd`, etc.).

## `inbox send` — content classification & capture shape

Classification order (mirrors the fallback-cascade style already used in `Add.execute`,
`src/Eru.Domain/Add.fs:270-311`): explicit `--as` override first, else:
1. Starts with `http://`/`https://` → **Url**.
2. `deps.ReadLocalFile` resolves it to `Some content` → **File** (existing file on disk).
3. Otherwise → **Message** (the literal string is the body).

Two different capture shapes, matching the guidance that `.meta.json` is for content eru didn't author:

- **Message / Url** (eru authors the `.md` itself) → write one `.md` file with inline YAML frontmatter
  carrying capture metadata, reusing OKF vocabulary already parsed elsewhere in the codebase
  (`Frontmatter.resource`, `Frontmatter.generated` : `ActorAt` — `src/Eru.Domain/Frontmatter.fs:60,88-89`):

  ```yaml
  ---
  type: raw
  resource: https://example.com/article   # or `resource: null` for a plain message
  generated:
    by: eru inbox send
    at: 2026-09-27T10:00:00Z
  ---

  <message body, or the URL (+ --note text) for a Url capture>
  ```

  `type: raw` satisfies `eru okf validate`'s only requirement (non-empty `type`) without forcing a
  Diataxis mode on unprocessed material — same idea as `concepts.md`'s "if it genuinely isn't
  documentation, use a different OKF type."

- **File** (an existing local file, of any kind) → copy its content **verbatim**, untouched, and write
  a companion `<name>.meta.json` sidecar next to it with exactly the two fields the ingestor contract
  already expects (`.agents/agents/ingestor.md:44-47`, existing example at
  `knowledge/inbox/archive/default/2026-09-18T220000-ripgrep-tips.meta.json`):
  ```json
  { "captured_at": "2026-09-27T10:00:00Z", "original_url": null }
  ```
  Never inject frontmatter into someone else's file (it may already have its own, or be non-text).
  v1 handles text content only (`ReadLocalFile`/`WriteLocalFile` are text-based); binary documents
  (PDFs, images) are out of scope — call this out as a known limitation.

**Filename**: `<compact-UTC-timestamp>-<slug>.<ext>`, e.g. `2026-09-27T101500-ripgrep-tips.md`,
matching the existing convention in `inbox/archive/default/*` (timestamp format `yyyy-MM-ddTHHmmss`,
no colons). `ext` is `.md` for Message/Url, or the original file's extension for File. `slug` is
`--title` if given, else derived:
- Message: first few words, lowercased, non-alphanumeric → `-`, capped ~60 chars, `"note"` if empty.
- Url: last non-empty path segment (query/fragment stripped), slugified; falls back to host.
- File: the source filename's stem, slugified.

If the resolved path already exists, append `-2`, `-3`, … until free (send twice in the same second
without `--title` should never clobber).

## `Deps` additions (`src/Eru.Domain/Deps.fs`)

- `DirectoryExists: string -> bool` — validates an inbox's configured `Path` is a real local
  directory, both at `inbox add` time and defensively at `inbox send` time (it may have moved/been
  deleted since).
- `GetUtcNow: unit -> System.DateTimeOffset` — testable clock (mirrors the existing `GetCwd` pattern),
  used for the timestamp in both the filename and the frontmatter/sidecar.

No new file-write primitive is needed: `Deps.WriteLocalFile`/`ReadLocalFile` already operate on
absolute paths as-is (`File.WriteAllText`/`File.Exists` in `src/Eru.Adapters/AdapterDeps.fs:15-21,60-64`
don't care whether the path is inside the current repo), so writing into an inbox directory needs no
new adapter plumbing beyond the two members above.

## Tests (`tests/Eru.Tests/`)

Using the existing `makeDeps`/`CapturedState` fake-`Deps` pattern seen in `AddTests.fs`, one test file
per new domain module plus config coverage:
- `ConfigTests.fs`: `Inboxes`/`DefaultInboxes` merge-by-key, `DefaultInbox` settings fallthrough, old
  config (no `inboxes` key) still loads.
- `InboxAddTests.fs`/`InboxRemoveTests.fs`/`InboxChannelAddTests.fs` etc.: mirror the existing
  `SourceAddTests.fs`-style CRUD coverage (dryrun, global vs local, missing-source/-inbox errors).
- `InboxSendTests.fs`:
  - classification: message vs. existing file vs. URL vs. `--as` override.
  - slug derivation for each content kind, including the empty/whitespace-message fallback.
  - filename collision → `-2` suffix.
  - Message/Url capture: exact frontmatter shape; dry-run doesn't write.
  - File capture: content copied verbatim; sidecar has exactly `{captured_at, original_url}`.
  - inbox not configured / inbox's path doesn't exist on disk → clear errors, nothing written.
  - default-inbox resolution: single configured inbox auto-selected; multiple without `-i`/`DefaultInbox`
    → error listing names.

## Docs

- `docs/reference/cli.md` — new `eru inbox` section (all six subcommands, flags table + examples).
- `docs/reference/config-file.md` — document `Inboxes`/`DefaultInboxes`, `InboxConfig`,
  `InboxChannelConfig`, and the new `DefaultInbox` setting.
- `skills/eru/references/commands.md` — add the command group alongside the others it already documents.
- Note in the PR/summary (not a code change): the knowledge repo's `ingestor.md` needs a small update to
  read `captured_at`/`resource` from frontmatter on eru-authored raw items, not just the `.meta.json`
  sidecar.

## Verification

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Inbox"

# manual smoke test against the real knowledge repo
cd /Users/devon.burriss/Documents/ws/dburriss/eru
dotnet run --project src/Eru -- inbox add knowledge /Users/devon.burriss/Documents/ws/dburriss/knowledge -g
dotnet run --project src/Eru -- inbox channel add knowledge eru --agent ingestor -g
dotnet run --project src/Eru -- inbox list
dotnet run --project src/Eru -- inbox send "ripgrep --hidden still respects .gitignore" --dryrun
dotnet run --project src/Eru -- inbox send "ripgrep --hidden still respects .gitignore"
dotnet run --project src/Eru -- inbox send https://example.com/some-article -n "why this matters"
dotnet run --project src/Eru -- inbox send ./todo.md -c eru
# then inspect knowledge/inbox/raw/default/*.md and knowledge/inbox/raw/eru/* by hand
```
