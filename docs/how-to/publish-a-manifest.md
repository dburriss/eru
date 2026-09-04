---
title: Publish a manifest from a knowledge source
type: how-to
tags: [manifest, source]
---

# Publish a manifest from a knowledge source

Use these steps in the repo that **publishes** knowledge — not the repo that consumes it. A manifest declares
which files a source exposes to consumers. See [manifests vs. collections](../explanation/concepts.md#manifests)
for the conceptual difference.

## 1. Create the manifest

```bash
eru manifest init
```

This creates `.eru/manifest.json` with `{ "version": 1, "files": [] }`. Use `--force` to overwrite an existing one.

## 2. Declare the files you want to expose

Paths can be exact files or gitignore-style globs:

```bash
eru manifest add "README.md" -t meta
eru manifest add "docs/*.md" -t docs -d "All documentation"
eru manifest add "templates/**/*.yaml" -t templates
```

Use `--dryrun` to preview an addition first.

## 3. Remove an entry

```bash
eru manifest remove "docs/*.md"
```

Removal matches by exact path/glob string, not by resolved files.

## 4. Verify the manifest before publishing

```bash
eru manifest verify
```

This resolves every entry against local files and reports any that match nothing, exiting with code 1 if any are
unresolved — useful as a CI check before merging changes to the manifest. Glob entries like `docs/*.md` must match
at least one file to pass.

## What happens on the consumer side

Consumers fetch and cache your manifest automatically:

- When they run `eru source add` against your repo.
- On every `eru sync` they run afterward.

Once cached, your manifest-advertised files become pullable via tags (`eru add --tag ...`), and appear in
`eru source files` and `eru search` for anyone who has registered your repo as a source.

See the [`eru manifest` reference](../reference/cli.md#eru-manifest) for the full flag list.
