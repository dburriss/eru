---
title: Generate docs from inbox changes with a GitHub Agentic Workflow
type: how-to
tags: [inbox, github-actions, gh-aw, ingestor, ci, security]
---

# Generate docs from inbox changes with a GitHub Agentic Workflow

[Locally](use-send-and-watch-locally.md), `eru inbox watch` runs the `ingestor` agent as raw items
arrive. To get the same curation from CI instead — so pushing a raw capture is enough, with no watcher
left running anywhere — wire up a [GitHub Agentic Workflow](https://github.github.com/gh-aw/) (`gh-aw`)
that triggers on changes under `inbox/raw/` and runs the same `ingestor.md` instructions.

This assumes a [knowledge repo already set up](set-up-a-knowledge-repo.md) with `.agents/agents/ingestor.md`
in place.

There's a simpler alternative: [a plain GitHub Action that calls `eru inbox process --all`
directly](generate-docs-from-inbox-with-a-plain-action.md) — no `gh-aw` dependency, and it reuses the
exact same code path as `eru inbox watch` locally instead of re-deriving it from `ingestor.md`. Read
["Why gh-aw instead of just calling eru"](#why-gh-aw-instead-of-just-calling-eru) below before picking
one; the short version is that `ingestor.md` grants its agent `Bash` access and processes raw captures
that aren't fully trusted input (a pasted URL, an article), and gh-aw is the one that keeps a live
write credential out of that agent's hands while it runs unattended.

## 1. Install the gh-aw CLI extension

```bash
gh extension install github/gh-aw
```

## 2. Write the workflow

Workflows are markdown, authored in `.github/aw/` and compiled to the `.lock.yml` GitHub Actions
actually runs. Create `.github/aw/process-inbox.md`:

```markdown
---
on:
  push:
    branches: [main]
    paths:
      - "inbox/raw/**"
permissions: read-all
safe-outputs:
  create-pull-request:
---
# Inbox Curator

Raw material was just pushed to `inbox/raw/`. Follow the instructions in
`.agents/agents/ingestor.md` at the repo root and curate every pending item
into structured notes, exactly as that file describes — including archiving
processed raw items to `inbox/archive/` and committing one change set per
item.
```

The frontmatter is the bounded, auditable surface: `permissions: read-all` keeps the agent itself
read-only, and `safe-outputs` names the one write it's allowed to request — here, a pull request
carrying the curated notes and archived raw items, rather than pushing straight to `main`. The
`ingestor.md` reference means this workflow definition doesn't duplicate the curation instructions —
it points at the same file `eru inbox process`/`watch` already use locally.

## 3. Compile it

```bash
gh aw compile
```

This generates `.github/workflows/process-inbox.lock.yml` — the file Actions runs. The `.md` is the
source you edit; the `.lock.yml` is the trusted, hardened artifact. Re-run `gh aw compile` any time you
change the markdown.

## 4. Commit both files

```bash
git add .github/aw/process-inbox.md .github/workflows/process-inbox.lock.yml
git commit -m "Add inbox curation workflow"
git push
```

## 5. Trigger it

Push (or merge) anything under `inbox/raw/` — for example, capture something with `eru inbox send`
from a machine that doesn't run `eru inbox watch`, then push:

```bash
eru inbox send "quick note" -i knowledge -c second-brain
git -C ~/code/knowledge add inbox/raw
git -C ~/code/knowledge commit -m "inbox: capture quick note"
git -C ~/code/knowledge push
```

The workflow runs, curates the item per `ingestor.md`, and opens a pull request with the new/updated
note, the updated `index.md`, and the archived raw file. Review and merge like any other PR.

## Why gh-aw instead of just calling eru

The obvious simpler design is a plain GitHub Actions workflow that installs `eru` and the channel's
configured ACP agent CLI, then runs `eru inbox process --all` directly — see [the plain-Action
version](generate-docs-from-inbox-with-a-plain-action.md) for exactly that. It's simpler, and it
reuses eru's actual code path instead of having a separate agent re-derive the same steps from
`ingestor.md`'s prose. So why not just do that here too?

Because `ingestor.md` grants its agent `tools: Read, Write, Edit, Bash, Grep, Glob`, and the raw
material it processes isn't fully trusted input — a captured URL, a pasted article, anything from the
open web. That's a prompt-injection surface: text in a raw capture could try to get the agent to do
something other than curate.

- **Locally**, that risk is contained because you're present to review before anything is pushed.
- **A plain Action** running `eru inbox process --all` unattended hands that same Bash-capable agent
  a live write credential (`GITHUB_TOKEN` or similar) for the whole job, even if the intent is only to
  use it at the very end to open a PR. An injected agent could try to use that token for something
  else mid-run.
- **gh-aw's `safe-outputs` model** specifically withholds write credentials from the agent's own
  process — it runs with `permissions: read-all` and no ambient push/PR token at all. It can only
  *request* a PR as structured output; a separate, narrowly scoped job (which never runs
  untrusted-content-driven Bash) is the only thing that actually holds write access, and it performs
  only the one action named under `safe-outputs`.

If this repo is one you're the only one capturing content into, and you're comfortable with that risk,
the plain-Action version is a reasonable and simpler choice. Pick gh-aw when the raw material could
plausibly include adversarial content, or you just want the stronger default.

## Notes

- **This replaces `watch`, it doesn't need it running too.** Running both `eru inbox watch` locally
  and this workflow isn't harmful (each processes independently, and `ingestor.md`'s dedupe/merge check
  in step 4 of its own instructions catches most overlap), but pick one as your primary loop to avoid
  double-processing races on the same raw item.
- **Widening the trigger.** To also curate other channels or paths, extend the `paths` filter (e.g.
  `inbox/raw/**` already covers every channel subfolder) rather than adding a second workflow.
