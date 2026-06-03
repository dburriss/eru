# Plan: Browse TUI — 5-Tab Redesign

## Context

The current `eru browse` TUI has two tabs: **Sources** (source tree with file nodes and tracked badges) and **Local Files** (flat list of lock file entries). The Sources tab does double duty — it is both a source browser and a file picker. This plan restructures the TUI into five tabs with clear, single-purpose responsibilities and a revised action set. Collections and Config are stubbed as placeholders for future implementation.

---

## Tab Structure

### Tab 1 — Files (default)

Browse sources and their files. Primary working view. Shows the source tree with `[✓]`/`[ ]` tracked badges on file nodes.

- Replaces the current **Sources** tab, renamed to **Files**
- `[✓]` nodes are tracked files (have a lock entry); `[ ]` nodes are available but not yet tracked

### Tab 2 — Sources

Manage knowledge sources. Shows sources only (no file children expanded by default).

- Add, refresh, and remove sources here

### Tab 3 — Local

Manage tracked files. Shows the lock file entries (current `LockPane`).

- Renamed from **Local Files**
- Disconnect and remove actions live here

### Tab 4 — Collections (stub)

Browse and manage collections — curated groups of file references that can be pulled as a unit. Collections contribute to what files are available, not what is locally tracked.

- Stub: renders a "Coming soon" placeholder view
- No actions wired yet

### Tab 5 — Config (stub)

View and edit eru configuration.

- Stub: renders a "Coming soon" placeholder view
- No actions wired yet

---

## Action Map

| Action | Key | Files | Sources | Local | Collections | Config |
|--------|-----|-------|---------|-------|-------------|--------|
| Add file to repo | `a` | ✓ (file nodes) | | | | |
| Refresh source index | `r` | ✓ (on parent source node) | ✓ | ✓ | | |
| Add source | `A` | | ✓ | | | |
| Remove source | `X` (Shift+x) | | ✓ (prompt) | | | |
| Search / filter | `/` | ✓ | ✓ | ✓ | | |
| Untrack + delete from repo (`eru remove`) | `x` | ✓ (`[✓]` nodes, prompt) | | ✓ (prompt) | | |
| Disconnect — remove lock entry, keep file (`eru disconnect`) | `d` | | | ✓ (prompt) | | |
| Quit | `q` | ✓ | ✓ | ✓ | ✓ | ✓ |

### Action semantics

- **`a` add** — pulls the file into the repo and records it in the lock file (`Add.execute`)
- **`r` refresh** — re-fetches source index from remote (`Sync.populateIndex`)
- **`A` add source** — opens the Add Source dialog (`SourceAdd.execute`)
- **`X` remove source** — prompts, then removes the source from config
- **`x` eru remove** — prompts, then deletes the file from the repo and removes the lock entry; file remains in the eru cache (`Remove.execute`)
- **`d` eru disconnect** — prompts, then removes only the lock entry; local file is kept in the repo (`Disconnect.execute`)

---

## Changes from Current Implementation

### `BrowseState.fs`

Update `ActiveTab` DU to 5 cases:

```fsharp
type ActiveTab = FilesTab | SourcesTab | LocalTab | CollectionsTab | ConfigTab
```

Add `RemoveSource of sourceName: string` to `BrowseAction` DU.

### `BrowseWindow.fs`

- Add tab labels for all 5 tabs
- Update `showTab` to cycle: Files → Sources → Local → Collections → Config → Files
- Wire `X` key + confirm dialog → `RemoveSource` action
- Stub panes for Collections and Config (simple `View` with a label)
- Update hint bar per tab

### `SourcesPane.fs` → split into `FilesPane.fs` + `SourcesPane.fs`

**`FilesPane.fs`** (was `SourcesPane.fs`):
- Remove `A` (Add Source) key binding
- Add `x` key binding on `[✓]` nodes → confirm dialog → `RemoveFile` action
- Hint bar: `[a] add  [x] remove  [r] refresh  [/] search  [tab] sources  [q] quit`

**`SourcesPane.fs`** (new — sources only):
- `TreeView<SourceNode>` with no file children
- `A` → Add Source dialog
- `X` → confirm dialog → `RemoveSource` action
- `r` → refresh selected source
- Hint bar: `[A] add source  [X] remove source  [r] refresh  [/] search  [tab] local  [q] quit`

### `LockPane.fs`

- Rename tab label to **Local**
- Replace `d` (no prompt) with `d` (prompt) → `Disconnect` action
- Replace `Del` key with `x` key → `RemoveEntry` action (already has prompt)
- Remove `A` (Add Source) key binding
- Hint bar: `[x] remove  [d] disconnect  [r] refresh  [/] search  [tab] collections  [q] quit`

### New stub panes

**`CollectionsPane.fs`** — placeholder `View` with "Collections — coming soon" label.

**`ConfigPane.fs`** — placeholder `View` with "Config — coming soon" label.

---

## Async Behaviour

Preserve the existing `Task.Run` + `Application.Invoke` pattern for the two long-running operations — do not make these synchronous:

- **`a` add** (`Add.execute`) — involves a git file fetch
- **`r` refresh** (`Sync.populateIndex`) — involves a git fetch and index rebuild

All other actions (`x`, `d`, `X` remove source, `A` add source) are synchronous in the current implementation and should remain so.

---

## Files to Create / Modify

| File | Change |
|------|--------|
| `Browse/BrowseState.fs` | Update `ActiveTab` DU; add `RemoveSource` to `BrowseAction` |
| `Browse/FilesPane.fs` | Rename from `SourcesPane.fs`; remove Add Source; add `x` with prompt |
| `Browse/SourcesPane.fs` | New — sources-only tree with `A`/`X`/`r` |
| `Browse/LockPane.fs` | Add prompt to `d`; change `Del` → `x`; remove `A`; update hint bar |
| `Browse/CollectionsPane.fs` | New stub pane |
| `Browse/ConfigPane.fs` | New stub pane |
| `Browse/BrowseWindow.fs` | 5-tab layout; updated tab cycling; action routing for `RemoveSource` |
| `Eru.Tui.fsproj` | Add new compile entries in dependency order |

---

## Implementation Sequence

1. Update `BrowseState.fs` — new `ActiveTab` cases and `RemoveSource` action
2. Create `FilesPane.fs` from `SourcesPane.fs` — remove Add Source, add `x` with prompt
3. Create new `SourcesPane.fs` — sources-only tree, `A`/`X`/`r` actions
4. Update `LockPane.fs` — prompt on `d`, `x` key, remove `A`, update hint bar
5. Create stub `CollectionsPane.fs` and `ConfigPane.fs`
6. Update `BrowseWindow.fs` — 5-tab layout, cycling, `RemoveSource` routing
7. Update `.fsproj` compile order
8. Verify: tab cycling, all key bindings, prompts on destructive actions

---

## Verification

```bash
dotnet build

# Tab cycling: Files → Sources → Local → Collections → Config → Files
dotnet run --project src/Eru -- browse

# Files tab
# - a on untracked file → adds to lock
# - x on tracked file → prompt → removes from repo + lock
# - r on source node → refreshes index
# - A key → no action (not available on Files tab)

# Sources tab
# - A → Add Source dialog works
# - X → prompt → source removed from config
# - r → refresh selected source

# Local tab
# - x → prompt → file deleted from repo, removed from lock
# - d → prompt → lock entry removed, file kept
# - r → re-pulls selected tracked file

# Collections tab
# - shows "coming soon" placeholder

# Config tab
# - shows "coming soon" placeholder
```
