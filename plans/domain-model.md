# Plan: eru Domain Model

Domain concepts for `eru`. Focus is on the ideas, not the underlying implementation.

## Core Concepts

### Knowledge Source
A place that canonical knowledge lives — a git repo, a local path, eventually an API. The domain treats it as an origin with an identity; the transport mechanism is an infrastructure concern.

### Artifact
The reusable thing pulled from a Knowledge Source. "File" is too narrow — an artifact could be a template, a script, a config fragment, a doc, a code snippet. Artifacts are produced with intent to be shared and reused.

### Library
A curated, named set of Artifacts. A Library implies ownership and intent — someone decided these artifacts belong together. Libraries can span multiple Knowledge Sources. Artifacts can belong to multiple Libraries.

### Tag
A flat label applied to Libraries or Artifacts for discovery and filtering. Tags are not hierarchical — there is no parent/child relationship. A thing can carry many tags. Tags are the primary way to navigate the knowledge space without knowing exact paths.

`eru search --tags dotnet observability` uses AND semantics — all specified tags must match.

### Manifest
The record of what this repo has pulled: every Artifact, where it came from, and in what state. The Manifest is the source of truth for the repo's relationship with its Knowledge Sources. Analogous to a lock file but the domain concept is richer — it carries Provenance, Status, and Sync Policy per entry.

### Provenance
The origin record on a Manifest entry — which Knowledge Source, which path within that source, at which Ref. Provenance is what makes Drift detectable and sync possible.

### Ref / Pin
The point in source history an Artifact was pulled from. An Artifact can track a source's HEAD (updated on sync) or be **Pinned** to a specific Ref (intentionally frozen). Pinning is explicit — the default is to track HEAD.

### Drift
An Artifact has drifted when its local content has diverged from its source. Drift is detected by comparing a stored content hash against the current source content. How drift is handled depends on the Sync Policy.

### Conflict
When both the local Artifact and the source have changed since the last pull. Conflicts can only arise under the `Mirror` Sync Policy — other policies either discard local changes or treat local as authoritative.

### Status
The aggregate health of the Manifest. Each Artifact entry is in one of these states:

| Status | Meaning |
|---|---|
| `Current` | Local matches source |
| `Drifted` | Local has diverged from source |
| `Conflicted` | Both local and source have changed (Mirror only) |
| `Missing` | Source no longer has this artifact |
| `Pinned` | Frozen intentionally; drift is not reported |

### Sync Policy
Governs the sync relationship for an Artifact. Set at pull time; can be overridden per artifact in the Manifest. Default is `Upstream`.

| Policy | Meaning | Drift handling |
|---|---|---|
| `Upstream` (default) | Source always wins | Drift is overwritten on sync |
| `Contribute` | Local changes flow back to source | Drift becomes a proposed change |
| `Mirror` | Bidirectional | Drift on both sides = Conflict |
| `Pinned` | No sync in either direction | Drift is ignored |

### Effective Configuration
The merged result of global and local config that the domain actually operates on. Sources, branch defaults, and settings all flow through this resolved view. It is not just a config detail — it is the input to every domain operation.

## Concept Relationships

```
Effective Configuration
  └── Knowledge Sources (ordered by priority)

Knowledge Source
  └── contains Artifacts
  └── contains Libraries (curated sets of Artifacts)

Library
  └── has Tags
  └── references Artifacts (possibly across multiple Sources)

Artifact
  └── has Tags
  └── pulled into repo → Manifest Entry

Manifest Entry
  └── Provenance (Source + path + Ref)
  └── Sync Policy
  └── Status (Current | Drifted | Conflicted | Missing | Pinned)
```

## Vocabulary Reference

| Concept | Notes |
|---|---|
| Knowledge Source | Origin of artifacts |
| Artifact | Reusable thing pulled from a source |
| Library | Curated named set of artifacts |
| Tag | Flat label for discovery and filtering |
| Manifest | What this repo has pulled + provenance + status |
| Provenance | Origin record on a manifest entry |
| Ref / Pin | Artifact frozen at a specific source version |
| Drift | Divergence between local artifact and source |
| Conflict | Both local and source changed (Mirror policy only) |
| Status | Per-artifact health state in the manifest |
| Sync Policy | Governs direction of sync relationship |
| Effective Configuration | Merged global + local config the domain operates on |
