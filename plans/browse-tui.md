# Plan: `eru browse` Interactive TUI Command

## Context

`eru` exposes knowledge sources and lock-file entries through static CLI commands (`source list`, `source files`, etc.). There is no way to navigate, search, or act on sources/entries interactively. This plan adds `eru browse` — a full TUI command using **Terminal.Gui v2** — that lets users navigate sources and installed files, search/filter, and perform key mutations (add, refresh, disconnect, remove, add source) without leaving the terminal.

Spectre.Console is kept for all existing static output; Terminal.Gui is introduced only for the browse command.

---

## Screens

### Screen 1 — Sources view (default when no `.eru/` in CWD)

```
╔══════════════════════════════════════════════════════════════════╗
║  eru browse                                          [q] Quit    ║
╠══════════════════════════════════════════════════════════════════╣
║ [Sources]  Lock  │  / Filter: _____________________________      ║
╠══════════════════╪═══════════════════════════════════════════════╣
║ Sources          │  Details                                      ║
║ ▶ my-source      │  Source : my-source                           ║
║ ▼ other-source   │  URL    : https://github.com/...              ║
║   ├ docs/api.md  │  Branch : main                                ║
║   └ docs/gui.md [INSTALLED]                                       ║
║                  │  File   : docs/api.md                         ║
║                  │  Hash   : 3fa2b1c4                            ║
║                  │  Tags   : dotnet, patterns                    ║
║                  │  Status : not installed                       ║
╠══════════════════╧═══════════════════════════════════════════════╣
║  a:Add  r:Refresh  A:Add source  Tab:Switch  /:Filter  q:Quit   ║
╚══════════════════════════════════════════════════════════════════╝
```

### Screen 2 — Lock view (default when `.eru/` exists in CWD)

```
╔══════════════════════════════════════════════════════════════════╗
║  eru browse                                          [q] Quit    ║
╠══════════════════════════════════════════════════════════════════╣
║  Sources  [Lock]  │  / Filter: _____________________________     ║
╠══════════════════════════════════════════════════════════════════╣
║  Source         Local Path             Status    Hash            ║
║  ────────────  ──────────────────────  ────────  ────────        ║
║  my-source     docs/guide.md           Current   3fa2b1c4        ║
║  my-source     docs/api.md             Drifted   a1b2c3d4        ║
║  other-source  patterns/adr.md         Missing   00ff1234        ║
╠══════════════════════════════════════════════════════════════════╣
║  d:Disconnect  Del:Remove  A:Add source  Tab:Switch  q:Quit      ║
╚══════════════════════════════════════════════════════════════════╝
```

Row colours: green = Current, yellow = Drifted, red = Missing.

---

## Keyboard Actions

| Key | Context | Action |
|-----|---------|--------|
| `a` | Sources view, file node selected | Add file to repo (`Add.execute`) |
| `A` | Either view | Open Add Source dialog |
| `r` | Sources view, any node | Refresh selected source index (`Sync.populateIndex`) |
| `d` | Lock view | Disconnect entry (`Disconnect.execute`) |
| `Del` | Lock view | Remove entry from lock + disk (`Remove.execute`) after confirm dialog |
| `/` | Either view | Focus filter bar |
| `Esc` | Filter bar focused | Clear filter, return focus to pane |
| `Tab` | Either view | Switch between Sources and Lock tabs |
| `q` / `Esc` | Window level | `Application.RequestStop()` |

---

## Architecture

### File structure

```
src/Eru.Cli/
  Args.fs                          ← add BrowseArgs + Browse case to EruArgs
  Browse/
    BrowseState.fs                 ← pure F# state types (no Terminal.Gui import)
    AddSourceDialog.fs             ← Dialog subclass for URL/name/branch/basepath input
    SourcesPane.fs                 ← FrameView with TreeView<SourceTreeNode>
    LockPane.fs                    ← FrameView with TableView (DataTable-backed)
    BrowseWindow.fs                ← Window composing both panes + filter bar + status bar
  BrowseCli.fs                     ← Cmd record, (|BrowseCmd|_|), run
  Program.fs                       ← add Browse dispatch arm
  Eru.Cli.fsproj                   ← add Terminal.Gui 2.* PackageRef + Compile entries
```

### View hierarchy

```
Application (static)
└── BrowseWindow : Window
      ├── SourcesPane : FrameView
      │     └── treeView : TreeView<SourceTreeNode>
      ├── LockPane : FrameView
      │     └── tableView : TableView (DataTable)
      └── AddSourceDialog : Dialog
            └── TextField inputs (url, name, branch, basepath)
```

### Pure state types (`BrowseState.fs`)

```fsharp
type ActiveTab = SourcesTab | LockTab

[<AbstractClass>]
type SourceTreeNode(label: string) =
    member _.Label = label

type SourceNode(src: SourceConfig, lockEntries: LockEntry list) =
    inherit SourceTreeNode(src.Name)
    member _.Source = src
    member _.LockEntries = lockEntries

type FileNode(row: SourceFileRow, sourceName: string, lockEntry: LockEntry option) =
    inherit SourceTreeNode(row.Path)
    member _.Row = row
    member _.SourceName = sourceName
    member _.LockEntry = lockEntry
    member _.IsInstalled = lockEntry.IsSome

type BrowseAction =
    | AddFile       of sourceName: string * remotePath: string
    | AddSource
    | RefreshSource of sourceName: string
    | Disconnect    of localPath: string
    | RemoveEntry   of localPath: string
```

### Data loading strategy

**Eager (before `Application.Run`):**
- `SourceList.execute deps` → `SourceRow list` (config + cached manifest, no network)
- `deps.ReadLockEntries cfg.StateFile` → `LockEntry list`
- `System.IO.Directory.Exists(".eru")` → determines initial tab

**Lazy (on TreeView node expand):**
- `SourceFiles.execute deps (Some sourceName)` called on first expand of a `SourceNode`
- Results cached in `Dictionary<string, SourceFileRow list>` in `SourcesPane`

**On `r` (refresh):** `Task.Run` + `Application.Invoke` to marshal back to UI thread after `Sync.populateIndex deps`.

### Action wiring pattern

Each pane fires `ActionRequested : IEvent<BrowseAction>`. `BrowseWindow` subscribes and routes to handlers:

```
AddFile(s, p)    → Add.execute deps args         → reload lock entries → refresh both panes
AddSource        → show AddSourceDialog          → SourceAdd.execute  → reload sources
RefreshSource(n) → Task.Run(Sync.populateIndex)  → reload tree node
Disconnect(p)    → Disconnect.execute deps args  → reload lock entries → refresh badges
RemoveEntry(p)   → confirm dialog → Remove.execute → reload lock entries → refresh badges
```

### Filter behaviour

Filter `TextField` is always visible in the tab bar row. `TextChanged` fires on every keystroke:
- **Sources view:** rebuild tree keeping only sources/files whose name contains the filter text; source nodes matching by name show all children
- **Lock view:** rebuild `DataTable` rows where `localPath`, `sourceName`, or `remotePath` contains text (case-insensitive)

### Key binding approach

Global keys: override `ProcessKeyDown` in `BrowseWindow`.  
Pane keys: subscribe to `this.KeyDown.Add(...)` in each pane constructor.  
`Key.A` is the letter; check `KeyEvent.IsShift` for uppercase `A` vs lowercase `a`.  
Background work: always `Task.Run` + `Application.Invoke` — Terminal.Gui v2 is single-threaded.

---

## Files Modified

| File | Change |
|------|--------|
| `src/Eru.Cli/Args.fs` | Add `BrowseArgs` DU (use `[<Hidden>] Placeholder` pattern like `McpArgs`) + `| Browse of ParseResults<BrowseArgs>` to `EruArgs` |
| `src/Eru.Cli/Eru.Cli.fsproj` | Add `<PackageReference Include="Terminal.Gui" Version="2.*" />` + Compile entries for Browse/ files in dependency order |
| `src/Eru.Cli/Program.fs` | Add `open Eru.Cli.BrowseCli` + `| BrowseCmd cmd -> BrowseCli.run deps cmd` |

New files (in order of compilation):
`Browse/BrowseState.fs`, `Browse/AddSourceDialog.fs`, `Browse/SourcesPane.fs`, `Browse/LockPane.fs`, `Browse/BrowseWindow.fs`, `BrowseCli.fs`

### Reused domain functions

| Function | File | Used for |
|----------|------|----------|
| `SourceList.execute` | `src/Eru.Domain/SourceList.fs` | Populate source tree |
| `SourceFiles.execute` | `src/Eru.Domain/SourceFiles.fs` | Lazy-load file nodes on expand |
| `Add.execute` | `src/Eru.Domain/Add.fs` | Add file action |
| `SourceAdd.execute` | `src/Eru.Domain/SourceAdd.fs` | Add source dialog confirm |
| `Sync.populateIndex` | `src/Eru.Domain/Sync.fs` | Refresh source index |
| `Remove.execute` | `src/Eru.Domain/Remove.fs` | Remove entry action |
| `Disconnect.execute` | `src/Eru.Domain/Disconnect.fs` | Disconnect entry action |
| `deps.ReadLockEntries` | `src/Eru.Domain/Deps.fs` | Load lock file |

---

## Implementation Sequence

1. **Scaffolding** — Add `BrowseArgs`, stub `BrowseCli.run`, wire in `Program.fs`. Confirm `dotnet build`.
2. **State types** — `BrowseState.fs` with `SourceTreeNode` hierarchy and `BrowseAction` DU.
3. **Lock pane** — `LockPane.fs` + DataTable binding + filter. Stub `BrowseWindow` to test in isolation.
4. **Sources pane** — `SourcesPane.fs` + `TreeView<SourceTreeNode>` + lazy `ITreeBuilder` + `[INSTALLED]` badges.
5. **Add Source dialog** — `AddSourceDialog.fs`.
6. **Window composition** — `BrowseWindow.fs`: tab switching, filter routing, action → domain → pane reload.
7. **Action handlers** — wire domain calls in `BrowseCli.run`, pass handlers into `BrowseWindow`.
8. **Context-sensitive start** — `.eru/` detection → initial tab selection.

---

## Pitfalls

- **`DataTable`:** rebuild fresh on each reload, don't mutate in-place.
- **`TreeView<T>` `ITreeBuilder`:** must be a concrete class (F# object expressions can't extend classes). `GetChildren` fires on node expand; cache per source name in `SourcesPane`.
- **Threading:** `Application.Invoke` is mandatory for any UI update from a background thread. `Sync.populateIndex` involves git I/O — always run via `Task.Run`.
- **Terminal.Gui v2 key constants:** verify `Key.Delete`, `Key.SlashKey` etc. against the v2 `Terminal.Gui.Key` enum — several names changed from v1.
- **No `async { }` at TUI entry:** `Application.Run` blocks the calling thread; use `Task.Run` only for background work spawned from key handlers.

---

## Verification

```bash
# Build
dotnet build

# No .eru/ in CWD → should open Sources view
dotnet run --project src/Eru -- browse

# With .eru/ in CWD → should open Lock view
dotnet run --project src/Eru -- browse

# Manual checks:
# - Tab switches between Sources and Lock views
# - / focuses filter bar, Esc clears it
# - a on a file node adds it; entry appears in eru.lock
# - d disconnects; entry removed from lock, local file kept
# - Del removes; entry removed from lock and local file deleted
# - A opens Add Source dialog; new source appears in tree
# - r refreshes source index; file list updates
```
