---
title: Getting started with eru
type: tutorial
tags: [setup, source, add, search]
---

# Getting started with eru

This tutorial walks through setting up eru in a project, registering a knowledge source, and pulling your first
file. By the end you'll have a working `.eru/` setup and understand the basic pull loop.

## 1. Scaffold a config

From the root of your repo:

```bash
eru init
```

This creates `.eru/config.json`. You now have a place to register knowledge sources and collections.

## 2. Register a knowledge source

A knowledge source is a git repo (or local path) that publishes shareable files:

```bash
eru source add https://github.com/my-org/knowledge
```

eru derives a source name from the URL, fetches its `.eru/manifest.json` (if it has one), and writes the source
into `.eru/config.json`.

Check it's registered:

```bash
eru source list
```

## 3. See what the source offers

```bash
eru source view knowledge
```

This shows the source's metadata and, if it publishes a manifest, the files it advertises.

## 4. Pull a file

```bash
eru add knowledge:docs/adr-template.md
```

The file is written into your repo and recorded in `.eru/eru.lock` — the state file that tracks everything eru
has pulled and where it came from.

## 5. Search across everything eru knows about

```bash
eru search adr
```

This searches manifest-advertised files, collections, and files already pulled into your repo.

## 6. Keep things up to date

Whenever you want to refresh source metadata and check for drift on files you've pulled:

```bash
eru sync
```

## Where to go next

- [How-to guides](../how-to/README.md) for specific tasks like curating collections or publishing a manifest.
- [Reference](../reference/README.md) for the full CLI reference and file formats.
- [Explanation](../explanation/README.md) to understand how sources, collections, manifests, and the lock file
  relate to each other.
