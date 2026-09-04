---
title: Pull files and collections
type: how-to
tags: [add, pull, source, collection, tag]
---

# Pull files and collections

## Pull a single file

```bash
# Paste a GitHub URL — source is auto-configured
eru add https://github.com/my-org/knowledge/blob/main/docs/adr-template.md

# Pull by source:path shorthand
eru add knowledge:docs/adr-template.md

# From the highest-priority configured source, by bare path
eru add docs/adr-template.md
```

## Control where the file lands locally

```bash
eru add docs/adr-template.md --target docs/    # keep filename, write into docs/
eru add docs/adr-template.md --target adr.md   # write to adr.md verbatim
```

See [where files land](../reference/lock-file-and-config.md#where-files-land) for the exact resolution rules.

## Pull a whole collection

```bash
eru add --collection onboarding-docs
eru add --collection knowledge:onboarding-docs   # restrict to one source
```

Each file in the collection is fetched and recorded as its own entry in `.eru/eru.lock`.

## Pull everything with a given tag

```bash
eru add --tag devops
eru search pipeline --tag ci --tag devops   # find first, then pull what you need
```

Tags use AND semantics — all specified tags must match.

## Preview before writing

Add `--dryrun` to any `eru add` invocation to see what would be pulled without writing anything:

```bash
eru add knowledge:docs/adr-template.md --dryrun
```

## Remove a file you no longer want tracked

```bash
eru remove docs/adr-template.md       # delete the file and its lock entry
eru disconnect docs/adr-template.md   # keep the file on disk, stop tracking it
```

Both accept the file's short hash instead of a path (shown by `eru search` and `eru source files`).

See also: [`eru add` reference](../reference/cli.md#eru-add), [`eru search` reference](../reference/cli.md#eru-search).
