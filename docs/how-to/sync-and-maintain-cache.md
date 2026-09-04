---
title: Keep a repo in sync and manage the cache
type: how-to
tags: [sync, cache, drift]
---

# Keep a repo in sync and manage the cache

## Refresh everything

```bash
eru sync
```

This refreshes all source metadata, rebuilds the local search index, caches collection content, and updates any
locally pulled files that have drifted from their upstream source. Use `--dryrun` to preview changes first.

Each lock file entry is reported as one of: **current**, **drifted** (overwritten), **missing** (remote gone), or
**skipped** (source not configured).

## Check what's stale without pulling anything

Use the read-only inspection commands — none of these touch the network by default:

```bash
eru source list                    # configured sources
eru source view <name>             # detail + manifest file list for one source
eru source files [<name>]          # files from the local index
eru search <terms>                 # search across everything eru knows about
```

Add `--refresh` to `eru source files` to force a network re-fetch for that source before displaying results. See
[inspecting state and search](../reference/inspecting-state-and-search.md) for exactly which data source backs
each command and how stale it can get.

## Clean up orphaned cache files

Files accumulate in the cache when entries are removed from a manifest or a source is deleted:

```bash
eru cache prune            # list orphans and prompt before deleting
eru cache prune --force    # delete without prompting
```

Safe to run at any time — it only removes files no longer referenced by the current index.

## Wipe the cache entirely

```bash
eru cache clear --dryrun   # preview what would be removed
eru cache clear            # prompt before deleting
eru cache clear --force    # delete without prompting
```

Run `eru sync` afterward to rebuild the cache from all configured sources.

See the [`eru sync` and `eru cache` reference](../reference/cli.md#eru-sync) for the full flag list.
