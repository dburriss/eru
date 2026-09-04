---
title: Curate a collection
type: how-to
tags: [collection, config]
---

# Curate a collection

A collection is a named, curated list of remote files stored in config (not in the lock file), letting you pull a
related set of files in one command. See [manifests vs. collections](../explanation/concepts.md#manifests) for how
this differs from a manifest.

## Create a collection

```bash
eru collection create onboarding-docs -d "Files every new engineer needs"
eru collection create adr-pack -t adr -t docs -g   # write to the global config
```

## Add files to it

```bash
eru collection add onboarding-docs -f knowledge:docs/adr-template.md
eru collection add adr-pack -f knowledge:KNOWLEDGE/adr/template.md -t adr
```

## Remove a file from it

```bash
eru collection remove onboarding-docs -f knowledge:docs/old-guide.md
```

## Choose local vs. global config

By default, collection commands write to `.eru/config.json` in the current repo. Pass `-g` to write to
`~/.config/eru/config.json` instead, making the collection available to every repo on the machine.

## Preview changes

Every collection command accepts `--dryrun` to preview the change without writing it:

```bash
eru collection remove adr-pack -f knowledge:KNOWLEDGE/adr/template.md --dryrun
```

## Pull a collection you've curated

```bash
eru add --collection onboarding-docs
```

See [pull files and collections](pull-files-and-collections.md) for more pulling options, and the
[`eru collection` reference](../reference/cli.md#eru-collection) for the full flag list.
