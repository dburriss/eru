---
title: Sharing agent skills across repos
type: tutorial
tags: [skills, agents, manifest, collection, sync]
---

# Sharing agent skills across repos

Agent skills — reusable, tool-agnostic instructions for coding agents — are just files, so eru can distribute
and keep them in sync the same way it does any other shared knowledge. This tutorial sets up one repo as the
canonical source of skills and pulls them into a consumer repo, following the convention used by the
[`npx skills`](https://github.com/vercel-labs/skills) ecosystem: a source repo publishes skills under a
top-level `skills/<name>/SKILL.md`, and each consumer installs them wherever their own agent expects (e.g.
`.claude/skills/` for Claude Code, `.agents/skills/` for OpenCode).

By the end you'll have:

- a source repo publishing skills from `skills/` via `.eru/manifest.json`
- a consumer repo pulling them into its agent's expected skills directory
- a repeatable `eru sync` step to pick up upstream updates

> **Note on Claude Code:** Claude Code only discovers skills under `.claude/skills/<name>/SKILL.md` — it
> doesn't look in the source repo's `skills/` layout or in `.agents/skills/`. That's fine: `skills/` in the
> source repo is a publishing convention, not a runtime one. Each consumer pulls (or symlinks) the files into
> whatever path its own agent needs; see the note at the end for Claude Code specifically.

## 1. Lay out the source repo

In your canonical skills repo (this tutorial uses `https://github.com/dburriss/knowledge` as an example),
skills live under a top-level `skills/<name>/SKILL.md`, one directory per skill — the layout `npx skills`
itself scans for:

```
knowledge/
  skills/
    code-review/
      SKILL.md
      checklist.md
    init/
      SKILL.md
```

## 2. Publish a manifest

Scaffold and populate `.eru/manifest.json` so other repos can discover what's available:

```bash
cd knowledge
eru manifest init
eru manifest add "skills/code-review/*.md" -t skill -t review -d "Code review checklist skill"
eru manifest add "skills/init/SKILL.md" -t skill -t bootstrap -d "Repo bootstrap skill"
eru manifest verify
```

`eru manifest verify` confirms every entry resolves to a real file before you commit.

```bash
git add .eru/manifest.json
git commit -m "Expose agent skills via eru manifest"
git push
```

## 3. Register the source in a consumer repo

In the repo that should receive the skills:

```bash
cd my-consumer-repo
eru init
eru source add https://github.com/dburriss/knowledge.git -n knowledge -b main
eru source view knowledge
```

`eru source view` lists the manifest entries so you can confirm the skill files are visible before pulling.

## 4. Group a multi-file skill into a collection

`code-review` is more than one file, so a collection lets you pull and track it as a unit:

```bash
eru collection create code-review-skill -t skill -t review -d "Full code-review skill"
eru collection add code-review-skill -f knowledge:skills/code-review/SKILL.md -t skill
eru collection add code-review-skill -f knowledge:skills/code-review/checklist.md -t skill
```

## 5. Pull the skills in

Point `-d` at whatever directory your consumer repo's agent actually reads. For a Claude Code repo:

```bash
eru add -c code-review-skill -d .claude/skills/code-review/
eru add knowledge:skills/init/SKILL.md -d .claude/skills/init/SKILL.md
```

For an OpenCode (or other AGENTS.md-compatible) repo, the same source pulls into `.agents/skills/` instead —
only the `-d` target changes:

```bash
eru add -c code-review-skill -d .agents/skills/code-review/
```

The trailing `/` on `-d` treats the target as a directory, so the whole collection lands under it. Both pulls
are recorded in `.eru/eru.lock` with a content hash per file.

## 6. Keep them up to date

Whenever the source repo's skills change, run this in each consumer repo:

```bash
eru sync --dryrun   # preview: upstream changes vs. any local edits
eru sync            # pull updates, or flag/restore local drift
```

Because each file is a separate lock entry, drift is reported file-by-file rather than skill-by-skill — for a
multi-file skill like `code-review`, that means you may see two drift entries instead of one.

## Making this work with Claude Code

Claude Code never reads the source repo's `skills/` directory directly — it only looks in its own
`.claude/skills/<name>/SKILL.md`. Since eru pulls files rather than mirroring a directory structure, this is
just a matter of the `-d` target used in step 5:

- **Pull straight to `.claude/skills/`.** As shown above — the source repo and manifest stay in the
  `npx skills`-compatible `skills/` layout; only the local consumer target changes.
- **Symlink locally instead**, if a repo needs to serve multiple agents from one pulled copy: pull into
  `.agents/skills/` and add `ln -s ../.agents/skills .claude/skills` so Claude Code resolves the same files.

Either way, the source repo's manifest paths stay under the standard `skills/` layout — the Claude-specific
accommodation belongs in the consumer repo, not upstream.

## Where to go next

- [Getting started](getting-started.md) if you haven't set up eru at all yet.
- [How-to guides](../how-to/README.md) for curating collections or publishing a manifest.
- [Reference](../reference/README.md) for the full CLI reference and file formats.
