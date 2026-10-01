---
title: Set up a knowledge repo
type: how-to
tags: [inbox, apm, skills, ingestor, knowledge-base]
---

# Set up a knowledge repo

A knowledge repo (a "second brain") is a plain git repository that holds curated notes, plus the
plumbing that turns raw captured material into those notes: `eru` for capture and sync, [APM](https://microsoft.github.io/apm/)
for installing the skills that do the curating, and an `ingestor` agent definition that describes how
curation should happen. This walks through setting one up from scratch.

See [dburriss/knowledge](https://github.com/dburriss/knowledge) for a working example of everything below.

## 1. Install the tools

```bash
dotnet tool install --global Eru.Tool
```

APM (Agent Package Manager) installs and manages the skills that operate on the repo. If you use
[mise](https://mise.jdx.dev/), pin it alongside dotnet in `mise.toml`:

```toml
[tools]
apm = "latest"
dotnet = "latest"
```

Otherwise follow the install instructions at [microsoft.github.io/apm](https://microsoft.github.io/apm/).

The `semantic-search` skill (below) shells out to [`ck`](https://github.com/BeaconBay/ck), a local semantic/lexical
search tool — install it too, e.g. via mise:

```toml
[tools]
"github:BeaconBay/ck" = "latest"
```

## 2. Initialise the repo

```bash
mkdir knowledge && cd knowledge
git init
eru init
apm init
```

`eru init` creates `.eru/config.json`, so this repo can itself act as a knowledge *source* other
projects pull from (`eru add`), and can also declare itself as a manifest with `eru manifest init`.

`apm init` scaffolds `apm.yml` — edit it to declare the skills and MCP servers this repo depends on:

```yaml
name: knowledge-tools
version: 0.1.0
description: Skills and MCP servers for extracting, searching, and organizing knowledge — document extraction (LiteParse), semantic search (ck), knowledge-file sync (eru), and Diataxis-based documentation organization.
includes: auto
targets:
  - claude
dependencies:
  apm:
    - run-llama/llamaparse-agent-skills/skills/liteparse
    - dburriss/eru/skills/eru
    - dburriss/eru/skills/semantic-search
    - dburriss/eru/skills/organizing-documentation
    - dburriss/eru/agents/ingestor.agent.md
  mcp:
    - name: eru
      registry: false
      transport: stdio
      command: dnx
      args: ["Eru.Tool", "--", "mcp"]
```

All five are published `apm` packages, so `apm install` resolves and fetches them automatically.
`semantic-search` (semantic search over the repo, via the `ck` CLI installed above),
`organizing-documentation` (the Diataxis classification skill), and `ingestor` (the curation agent —
see [step 4](#4-wire-up-the-ingestor-agent)) all live in
[eru's own `skills/`/`agents/`](https://github.com/dburriss/eru), alongside `skills/eru` itself,
since curating captured material is squarely inside eru's remit. Then run:

```bash
apm install
```

This resolves `apm.yml` into `apm.lock.yaml` (commit both) and installs the referenced skills —
by convention under `.apm/skills/` — plus the `eru` MCP server config for the target listed under
`targets`.

## 3. Register an inbox

An inbox is a local directory `eru inbox send` writes captured messages, files, and URLs into. Register
this repo as one, from anywhere on your machine, using the global config so it isn't tied to any one
project:

```bash
eru inbox add knowledge ~/code/knowledge --default-channel second-brain -g
```

Register the **root of the knowledge repo** as the inbox path, not its `inbox/` subfolder. `eru inbox
process`/`watch` launch the agent with that path as its working directory, so the root is where the agent
harness discovers the repo's installed skills and agents (e.g. `.claude/skills/`, `.agents/skills/`).

This creates `inbox/raw/` and `inbox/archive/` under the repo (raw captures land in the former;
curated items are archived to the latter — see [use send and watch locally](use-send-and-watch-locally.md)).

## 4. Wire up the ingestor agent

`eru inbox process`/`eru inbox watch` curate raw items by running a configured agent over the [Agent
Client Protocol](https://agentclientprotocol.com) and prepending an instructions file to its prompt.
By convention eru looks for that file at `<inbox>/.agents/agents/ingestor.md` automatically — but
since `ingestor` above was installed via `apm` rather than hand-written, it lands wherever `apm`
puts agent primitives for your target instead: `.claude/agents/ingestor.md` for `claude`,
`.opencode/agents/ingestor.md` for `opencode`, and so on (see the
[targets matrix](https://microsoft.github.io/apm/reference/targets-matrix/)). Point eru at that path
explicitly with `--agent-instructions` when wiring the channel:

```bash
eru inbox channel add knowledge second-brain --agent-command claude --agent-args --print --agent-args acp \
  --agent-instructions .claude/agents/ingestor.md
```

> A future eru release is expected to also check the `apm`-installed path automatically, at which
> point `--agent-instructions` becomes optional again for this case — for now, pass it explicitly.

See [`eru inbox channel add`](../reference/cli.md#eru-inbox-channel-add) for the full flag set. If
you'd rather hand-write your own curation instructions instead of using the packaged `ingestor` agent,
skip the `apm.yml` dependency above and write `.agents/agents/ingestor.md` directly — eru picks that
up with no `--agent-instructions` flag needed.

## 5. Verify

```bash
eru inbox send "test capture" -i knowledge -c second-brain
eru inbox process --dryrun -i knowledge
```

The dry run should show the item you just sent and the agent/instructions that would curate it. Once
that looks right, drop `--dryrun` to actually curate it, or move on to
[using send and watch locally](use-send-and-watch-locally.md) for the everyday workflow.
