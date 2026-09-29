---
title: Use send and watch locally on a knowledge repo
type: how-to
tags: [inbox, send, watch, ingestor]
---

# Use send and watch locally on a knowledge repo

Once a [knowledge repo is set up](set-up-a-knowledge-repo.md) with an inbox and an `ingestor` agent,
`eru inbox send` and `eru inbox watch` are the two commands you'll use day to day: one to capture,
one to curate continuously.

## Capture with `inbox send`

Send a quick note, a URL, or an existing file into the inbox from wherever you're working — no need
to `cd` into the knowledge repo first, since `inbox send` only needs the global config:

```bash
eru inbox send "ripgrep --hidden still respects .gitignore"
eru inbox send https://example.com/some-article -n "why this matters"
eru inbox send ./notes.md -c second-brain
pbpaste | eru inbox send
```

`-i <inbox>` picks the inbox by name if you have more than one configured; with only one, it
auto-resolves. `-c <channel>` picks the channel — omit it and the inbox's default channel is used.
Each send lands under `inbox/raw/<channel>/` as a timestamped `.md` (with `type: raw` frontmatter) or,
for a file capture, the file itself plus a `.meta.json` sidecar.

Use `--dryrun` to see where something would land without writing it:

```bash
eru inbox send "quick note" --dryrun
```

## Curate continuously with `inbox watch`

Rather than remembering to run `eru inbox process` after every capture, start a watcher once and leave
it running — it processes every pending item automatically as it arrives:

```bash
eru inbox watch -i knowledge
```

This resolves the inbox/channel scope once, then blocks, reacting to new raw items via a filesystem
watcher (with a periodic poll as fallback — default every 30s, tune with `--interval <seconds>` or
`inboxWatchIntervalSeconds` in [config](../reference/config-file.md)). A short debounce means a burst of
captures triggers one processing pass, not one per file.

```bash
eru inbox watch -c second-brain --interval 10
eru inbox watch --dryrun   # log what would be processed, without spawning an agent
```

A few things worth knowing before you leave it running:

- **It keeps going through failures.** A failing item is logged and left in `raw/` to retry on the
  next trigger — the watch loop itself doesn't stop. Check the logs periodically rather than assuming
  silence means success.
- **It's foreground and long-running.** There's no built-in daemonizing — use a terminal tab, `tmux`,
  or a process supervisor (`launchd`/`systemd`) if you want it surviving a logout. `Ctrl+C` shuts it
  down gracefully, letting the current item finish first.
- **Only channels with an agent configured are watched by default.** A channel with no
  `--agent-command` set (see [set up a knowledge repo](set-up-a-knowledge-repo.md#4-add-the-ingestor-agent))
  is invisible to `watch` unless you pass `-c` explicitly.
- **Add `--debug` at the top level** (`eru --debug inbox watch ...`) to see each item's ACP handshake
  timing breakdown (`initialize`/`session/new`/`prompt`) — useful for telling fixed per-item overhead
  apart from the agent actually thinking on a slow item.

## One-off processing instead

If you'd rather curate on demand — reviewing what's pending before it runs — use `inbox process`
instead of `watch`:

```bash
eru inbox process --dryrun     # see what would happen
eru inbox process              # curate the oldest pending item
eru inbox process --all        # curate everything pending, oldest first, stop at first failure
eru inbox process some-item -c second-brain   # curate one specific item
```

`watch` is exactly this `--all` logic re-run automatically on every new arrival — pick whichever
matches how hands-off you want the loop to be. See [`eru inbox`](../reference/cli.md#eru-inbox) for
the full reference.
