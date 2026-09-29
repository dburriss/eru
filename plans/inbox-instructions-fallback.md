---
status: proposed
---

# Plan: fallback chain for `eru inbox process`'s agent instructions resolution

## Context

`eru inbox process`/`watch` resolves a channel's curation instructions via
`resolveInstructions` in `src/Eru.Domain/InboxProcess.fs:129-140` (constant
`defaultInstructionsRelPath` at line 125). Today this only checks two things:
an explicit `AgentConfig.InstructionsPath` (hard error if it doesn't
resolve), or — implicitly, when unset — `<inbox>/.agents/agents/ingestor.md`.
If neither resolves, the agent currently gets **no instructions prepended at
all** (`Ok None`, documented as "not an error" — `buildPrompt` just sends the
bare raw item content).

This leaves two gaps:

1. Coding-agent CLIs each already have their own well-known home for a
   project's agent/subagent definitions (Claude Code, opencode,
   cursor-agent, codex, copilot). A channel's `--agent-command` already
   tells us which tool is running it — eru should check that tool's
   convention path too, before giving up.
2. Even when nothing at all resolves, an agent should never be left with
   *zero* curation instructions. Eru already ships its own real ingestor
   agent definition at repo root (`agents/ingestor.agent.md` — the same file
   Claude Code itself uses via `.claude/agents/ingestor.agent.md`). This
   becomes the final, always-succeeding fallback tier: embedded into the
   `Eru.Domain` assembly as a single source of truth, not duplicated as a
   hardcoded string.

A related idea — a *per-channel-folder* convention file living directly
inside `inbox/raw/<channel>/` — was considered and dropped: per-channel
override already exists today via `AgentConfig.InstructionsPath` (`eru inbox
channel add --agent-instructions <path>`), so a second zero-config
convention for the same thing would be redundant. It would also have
required new exclusion logic: `listChannelItems` (`InboxProcess.fs:79-93`)
lists every non-hidden, non-`.meta.json` file in `inbox/raw/<channel>/` as a
pending raw item with no extension or naming-convention check — a fixed-name
file dropped in that same directory would otherwise be picked up, curated,
and archived as if it were a real captured item. Out of scope here.

The explicit-`InstructionsPath` branch is unaffected by this plan — it stays
exactly as-is, hard error and all.

## Resolution order (implicit `InstructionsPath` only)

1. `<inbox>/.agents/agents/ingestor.md` — existing, unchanged.
2. An apm-managed path derived from `AgentConfig.Command`'s basename
   (directory components and a trailing `.exe` stripped, case-insensitive;
   `Args` ignored entirely — it's the executable identity that determines
   the tool, not how it's invoked). Fixed table, name always `ingestor`:
   ```
   claude       -> ".claude/agents/ingestor.md"
   opencode     -> ".opencode/agents/ingestor.md"
   cursor-agent -> ".cursor/agents/ingestor.md"
   codex        -> ".codex/agents/ingestor.md"
   copilot      -> ".github/agents/ingestor.agent.md"
   ```
   Combined with `inbox.Path` the same way as tier 1. An unmatched command
   (e.g. a custom wrapper script) skips straight to tier 3.
3. Eru's own built-in curation instructions — the real, checked-in
   `agents/ingestor.agent.md` (repo root), embedded into `Eru.Domain.dll` as
   an `EmbeddedResource` and read via `Assembly.GetManifestResourceStream` at
   runtime. Always succeeds, so an implicit `InstructionsPath` never leaves
   an agent with no instructions.

Since tier 3 always yields content, `Ok None` becomes dead in the implicit
branch (the explicit branch never produced `None` either) — simplify
`resolveInstructions`'s return type from `Result<string option, string>` to
`Result<string, string>` and drop `buildPrompt`'s `Ok None -> Ok itemContent`
branch. `resolveInstructions` is `private`, so this has exactly one call
site.

## Implementation

### 1. `src/Eru.Domain/Eru.Domain.fsproj`

Add a new `ItemGroup` (after the existing `Compile` one):

```xml
  <ItemGroup>
    <EmbeddedResource Include="../../agents/ingestor.agent.md" LogicalName="Eru.Domain.DefaultIngestorInstructions.md" />
  </ItemGroup>
```

`../../agents/ingestor.agent.md` is the correct relative path from
`src/Eru.Domain/` to the repo-root `agents/` folder. `LogicalName` is pinned
explicitly since MSBuild's default logical-name derivation for an
out-of-tree `Include` path isn't reliable to depend on. This is the first
`EmbeddedResource` anywhere in the repo — no existing pattern to match.

### 2. `src/Eru.Domain/InboxProcess.fs`

- Add `open System.Reflection`.
- Add `apmInstructionsPaths : Map<string,string>` (table above) and a
  `commandBasename (command: string) : string` helper (strip dir via
  `Path.GetFileName`, strip trailing `.exe` case-insensitively).
- Add a module-level `Lazy<string>` that reads the embedded resource once,
  via `Assembly.GetExecutingAssembly().GetManifestResourceStream("Eru.Domain.DefaultIngestorInstructions.md")`,
  `failwith`ing only if the resource is unexpectedly missing (a build
  misconfiguration, not a runtime/user error).
- Rewrite `resolveInstructions` to `Result<string, string>`: explicit branch
  copied verbatim (same rooted/relative logic, same error message); implicit
  branch chains tier 1 → tier 2 (via `commandBasename` + table lookup) →
  tier 3 (the lazy embedded default), each `deps.ReadLocalFile` `Error`
  propagating immediately.
- Update `buildPrompt` (~line 159-163) to match the new `Ok string` shape,
  removing the now-impossible `Ok None` case.

### 3. Docs / usage strings to update in sync

- `src/Eru.Cli/Args.fs:241` — `Agent_Instructions` usage string: mention the
  apm-path and built-in-default tiers alongside the existing default path.
- `docs/reference/cli.md:300-306` (`eru inbox channel add`) — replace the
  "not an error, no instructions prepended" prose with the 3-tier chain
  description (list all 5 apm commands and their paths), keeping the
  "explicit path missing = error" note unchanged.
- `docs/reference/config-file.md` (`InstructionsPath` row) — same update for
  the config-file-driven path.
- `src/Eru.Domain/Config.fs:70-76` (`InstructionsPath` doc comment) — update
  to describe the full fallback chain instead of "proceeds with no
  instructions."

## Tests

`tests/Eru.Tests/InboxProcessTests.fs`, "Agent instructions prepending"
section (existing lines ~331-389):

- **Default `.agents/agents/ingestor.md` used when present**: no change —
  tier 1 still short-circuits first.
- **"prompt is just the item content when no instructions file exists
  anywhere"**: premise is obsolete — rename/rewrite. Change the channel's
  command from `"opencode"` (now a matched apm entry) to an unmatched
  command like `"my-wrapper.sh"`, so the test unambiguously exercises "both
  tier 1 and tier 2 miss → tier 3 fires," and assert the prompt
  `Assert.Contains` a stable substring from the real embedded file — e.g.
  `"You curate raw captured material in \`inbox/raw/\`"` (the document's
  topic sentence, confirmed present verbatim in `agents/ingestor.agent.md`)
  — plus the item content.
- **Explicit path overrides default / explicit path missing = error**: no
  change — explicit branch untouched, assertions are on prompt/error strings
  only.
- **New test**: apm-path tier match — `acpAgent "claude"` with a file at
  `/kb/.claude/agents/ingestor.md`, no `.agents/agents/ingestor.md` present;
  assert that content is used and the embedded-default substring is absent.
- **New test**: tier 1 wins over a matching apm path when both are present
  (`acpAgent "claude"` with both `/kb/.agents/agents/ingestor.md` and
  `/kb/.claude/agents/ingestor.md` populated) — asserts ordering explicitly
  rather than incidentally.
- Optionally cover `commandBasename` directly (full path / `.exe` variants
  of `"claude"` all resolving the same apm entry) as a `[<Theory>]`.

## Critical files

- `src/Eru.Domain/Eru.Domain.fsproj`
- `src/Eru.Domain/InboxProcess.fs`
- `src/Eru.Domain/Config.fs`
- `src/Eru.Cli/Args.fs`
- `docs/reference/cli.md`, `docs/reference/config-file.md`
- `tests/Eru.Tests/InboxProcessTests.fs`

## Verification

```bash
dotnet build
# confirm the embedded resource is present:
# Assembly.LoadFrom(...).GetManifestResourceNames() -> "Eru.Domain.DefaultIngestorInstructions.md"

dotnet test

# manual sanity check: no .agents/ or .claude/ under the inbox
eru inbox channel add <inbox> <channel> --agent-command claude
eru inbox process --dryrun
# confirm the prompt built for the agent includes eru's built-in ingestor
# instructions rather than a bare item
```
