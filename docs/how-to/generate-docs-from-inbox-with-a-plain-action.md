---
title: Generate docs from inbox changes with a plain GitHub Action
type: how-to
tags: [inbox, github-actions, ingestor, ci]
---

# Generate docs from inbox changes with a plain GitHub Action

[Locally](use-send-and-watch-locally.md), `eru inbox watch` runs the `ingestor` agent as raw items
arrive. To get the same curation from CI instead, run `eru inbox process --all` in an ordinary
GitHub Actions workflow triggered on changes under `inbox/raw/` — no extra tooling beyond eru itself
and the channel's already-configured agent.

This is simpler than [the gh-aw version](generate-docs-from-inbox-with-gh-aw.md) and reuses the exact
same code path as `eru inbox watch` locally, instead of having a separate agent re-derive the same
steps from `ingestor.md`'s prose. The tradeoff is what credentials the curating agent holds while it
runs unattended — see [Security tradeoff](#security-tradeoff-vs-gh-aw) below before choosing this over
gh-aw.

This assumes a [knowledge repo already set up](set-up-a-knowledge-repo.md) with an inbox, a channel
with an agent configured (`eru inbox channel add ... --agent-command ...`), and either
`.agents/agents/ingestor.md` in place or the packaged `ingestor` apm agent wired up per that doc.

## 1. Write the workflow

Create `.github/workflows/process-inbox.yml`:

```yaml
name: Process inbox

on:
  push:
    branches: [main]
    paths:
      - "inbox/raw/**"

permissions:
  contents: write
  pull-requests: write

jobs:
  process-inbox:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"

      - name: Install eru
        run: dotnet tool install --global Eru.Tool

      - name: Install the channel's agent CLI
        run: npm install -g @anthropic-ai/claude-code  # or whatever --agent-command names

      - name: Process every pending item
        env:
          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}
        run: eru inbox process --all -i knowledge

      - uses: peter-evans/create-pull-request@v6
        with:
          title: "inbox: curate pending captures"
          commit-message: "inbox: curate pending captures"
          branch: inbox-curation
          add-paths: |
            inbox/
            **/*.md
```

Swap the "Install the channel's agent CLI" step for whatever `--agent-command` the channel is actually
configured with, and add whichever credential secret that agent needs (`ANTHROPIC_API_KEY` for
`claude`, an OpenAI key for `codex`, etc.) under **Settings → Secrets and variables → Actions**.

`eru inbox process --all` does the same archiving-and-committing it would do if you ran it by hand:
each curated item lands as a local commit on the runner's checkout. `create-pull-request` then picks
up those local commits (not a live diff it computes itself) and pushes them as a branch + PR, rather
than letting the job push straight to `main`.

## 2. Commit the workflow

```bash
git add .github/workflows/process-inbox.yml
git commit -m "Add inbox curation workflow"
git push
```

## 3. Trigger it

```bash
eru inbox send "quick note" -i knowledge -c second-brain
git -C ~/code/knowledge add inbox/raw
git -C ~/code/knowledge commit -m "inbox: capture quick note"
git -C ~/code/knowledge push
```

The workflow runs `eru inbox process --all`, which curates every pending item exactly as it would
locally, then opens a pull request with the resulting commits. Review and merge like any other PR.

## Security tradeoff vs gh-aw

`ingestor` (whether hand-written or the packaged apm agent) runs with `Bash`/`Write` access, and the
raw material it curates isn't fully trusted input — a captured URL, a pasted article, anything from
the open web. That's a prompt-injection surface: text in a raw capture could try to get the agent to
do something other than curate.

In this design, the whole job — including that Bash-capable agent process — holds `contents: write`
and `pull-requests: write` for its entire runtime, even though the intent is only to use that access at
the final `create-pull-request` step. A successfully injected agent could attempt to use the ambient
`GITHUB_TOKEN` for something else first.

[The gh-aw version](generate-docs-from-inbox-with-gh-aw.md) avoids this by running the agent with no
write credentials at all (`permissions: read-all`), and having a separate, narrowly scoped job perform
the one permitted write after the fact. That's more moving parts and an extra CLI dependency
(`gh-aw`), for a real security improvement. This plain-Action version is a reasonable choice if you're
the only one capturing content into this repo and are comfortable with that risk; reach for gh-aw
instead if the raw material could plausibly include adversarial content, or you just want the stronger
default.

## Notes

- **This replaces `watch`, it doesn't need it running too.** Running both `eru inbox watch` locally
  and this workflow isn't harmful (each processes independently, and `ingestor.md`'s dedupe/merge check
  catches most overlap), but pick one as your primary loop to avoid double-processing races on the
  same raw item.
- **`--all` stops at the first failure.** A failing item leaves the rest of the batch unprocessed for
  this run; it'll be picked up on the next trigger. Check the job log if a PR doesn't show up as
  expected.
