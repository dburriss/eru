module Eru.Tests.InboxTests

open Xunit
open Eru

// ── Test helpers ─────────────────────────────────────────────────────────────

type private CapturedState = {
    mutable WrittenLocalConfig  : LocalConfig option
    mutable WrittenGlobalConfig : GlobalConfig option
}

let private newState () : CapturedState = { WrittenLocalConfig = None; WrittenGlobalConfig = None }

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (existingDirs: string list)
    (state: CapturedState) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        WriteLocalConfig   = fun cfg -> state.WrittenLocalConfig <- Some cfg; Ok ()
        WriteGlobalConfig  = fun cfg -> state.WrittenGlobalConfig <- Some cfg; Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent = fun _ _ paths -> Ok (paths |> List.map (fun p -> (p, $"content:{p}")))
        ListRemoteTopLevel = fun _ _ -> Ok []
        ListRemoteFiles    = fun _ _ _ -> Ok []
        WriteLocalFile     = fun _ _ -> Ok ()
        ReadLocalFile      = fun _ -> Ok None
        DeleteLocalFile    = fun _ -> Ok ()
        HashContent        = fun s -> $"sha256:{s}"
        GetCwd             = fun () -> "/tmp"
        ReadCachedManifest      = fun _ -> Ok None
        CacheSourceManifest     = fun _ _ -> Ok ()
        ReadLocalManifest       = fun () -> Ok None
        WriteLocalManifest      = fun _ -> Ok ()
        ResolveLocalGlob        = fun _ -> []
        ReadSourceIndex         = fun _ -> Ok None
        WriteSourceIndex        = fun _ _ -> Ok ()
        CacheSourceContent      = fun _ _ _ -> Ok "files/fakehex"
        ReadCachedSourceContent = fun _ _ -> Ok None
        BuildSearchIndex        = fun _ _ -> ()
        ParseYamlBlock          = fun _ -> Ok Yaml.Null
        ListMarkdownFiles       = fun _ -> Ok []
        ExtractLinks            = fun _ -> []
        GetRemoteHeadSha        = fun _ _ -> Error "not implemented"
        DirectoryExists         = fun p -> List.contains p existingDirs
        GetUtcNow               = fun () -> System.DateTimeOffset(2026, 9, 27, 10, 0, 0, System.TimeSpan.Zero)
    }

let private makeInbox path : InboxConfig =
    { Path = path; RawPath = None; DefaultChannel = None; Channels = Map.empty }

let private assertError (result: Result<'a, string>) = match result with Ok _ -> Assert.Fail "Expected Error result" | Error _ -> ()

// ── InboxAdd ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``InboxAdd errors when path is not an existing directory`` () =
    let state = newState ()
    let deps = makeDeps None (Some { Version = 1; Sources = []; Collections = []; Inboxes = Map.empty; Settings = None }) [] state
    let cmd : InboxAdd.Command = { Name = "kb"; Path = "/nope"; RawPath = None; DefaultChannel = None; IsGlobal = false; DryRun = false }
    InboxAdd.execute deps cmd |> assertError

[<Fact>]
let ``InboxAdd writes to local config by default`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.empty; Settings = None }
    let deps = makeDeps None (Some local) [ "/kb" ] state
    let cmd : InboxAdd.Command = { Name = "kb"; Path = "/kb"; RawPath = None; DefaultChannel = None; IsGlobal = false; DryRun = false }
    InboxAdd.execute deps cmd |> ignore
    Assert.True(state.WrittenLocalConfig.IsSome)
    Assert.Equal("/kb", state.WrittenLocalConfig.Value.Inboxes["kb"].Path)

[<Fact>]
let ``InboxAdd writes to global config when IsGlobal`` () =
    let state = newState ()
    let deps = makeDeps (Some { Version = 1; DefaultSources = []; Collections = []; DefaultInboxes = Map.empty; Defaults = None }) None [ "/kb" ] state
    let cmd : InboxAdd.Command = { Name = "kb"; Path = "/kb"; RawPath = None; DefaultChannel = None; IsGlobal = true; DryRun = false }
    InboxAdd.execute deps cmd |> ignore
    Assert.True(state.WrittenGlobalConfig.IsSome)
    Assert.Equal("/kb", state.WrittenGlobalConfig.Value.DefaultInboxes["kb"].Path)

[<Fact>]
let ``InboxAdd errors when name already exists`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", makeInbox "/kb" ]; Settings = None }
    let deps = makeDeps None (Some local) [ "/kb" ] state
    let cmd : InboxAdd.Command = { Name = "kb"; Path = "/kb"; RawPath = None; DefaultChannel = None; IsGlobal = false; DryRun = false }
    InboxAdd.execute deps cmd |> assertError

[<Fact>]
let ``InboxAdd dryrun does not write`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.empty; Settings = None }
    let deps = makeDeps None (Some local) [ "/kb" ] state
    let cmd : InboxAdd.Command = { Name = "kb"; Path = "/kb"; RawPath = None; DefaultChannel = None; IsGlobal = false; DryRun = true }
    InboxAdd.execute deps cmd |> ignore
    Assert.True(state.WrittenLocalConfig.IsNone)

// ── InboxList ────────────────────────────────────────────────────────────────

[<Fact>]
let ``InboxList merges local and global, local taking precedence by name`` () =
    let state = newState ()
    let g = Some { Version = 1; DefaultSources = []; Collections = []; DefaultInboxes = Map.ofList [ "kb", makeInbox "/g/kb"; "other", makeInbox "/g/other" ]; Defaults = None }
    let l = Some { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", makeInbox "/l/kb" ]; Settings = None }
    let deps = makeDeps g l [] state
    let rows = InboxList.execute deps |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(2, rows.Length)
    let kb = rows |> List.find (fun r -> r.Name = "kb")
    Assert.Equal("/l/kb", kb.Path)
    Assert.Equal("local", kb.Scope)
    let other = rows |> List.find (fun r -> r.Name = "other")
    Assert.Equal("global", other.Scope)

// ── InboxRemove ──────────────────────────────────────────────────────────────

[<Fact>]
let ``InboxRemove removes from local config`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", makeInbox "/kb" ]; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let cmd : InboxRemove.Command = { Name = "kb"; IsGlobal = false; DryRun = false }
    InboxRemove.execute deps cmd |> ignore
    Assert.True(state.WrittenLocalConfig.Value.Inboxes.IsEmpty)

[<Fact>]
let ``InboxRemove errors when inbox not found`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.empty; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let cmd : InboxRemove.Command = { Name = "missing"; IsGlobal = false; DryRun = false }
    InboxRemove.execute deps cmd |> assertError

// ── InboxChannelAdd / List / Remove ─────────────────────────────────────────

[<Fact>]
let ``InboxChannelAdd adds a channel to the owning (local) config`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", makeInbox "/kb" ]; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let cmd : InboxChannelAdd.Command = { InboxName = "kb"; ChannelName = "eru"; Agent = Some "ingestor"; Description = None; DryRun = false }
    InboxChannelAdd.execute deps cmd |> ignore
    let updated = state.WrittenLocalConfig.Value.Inboxes["kb"]
    Assert.True(updated.Channels.ContainsKey "eru")
    Assert.Equal(Some "ingestor", updated.Channels["eru"].Agent)

[<Fact>]
let ``InboxChannelAdd writes back to global config when inbox owned there`` () =
    let state = newState ()
    let g = Some { Version = 1; DefaultSources = []; Collections = []; DefaultInboxes = Map.ofList [ "kb", makeInbox "/kb" ]; Defaults = None }
    let deps = makeDeps g None [] state
    let cmd : InboxChannelAdd.Command = { InboxName = "kb"; ChannelName = "eru"; Agent = None; Description = None; DryRun = false }
    InboxChannelAdd.execute deps cmd |> ignore
    Assert.True(state.WrittenGlobalConfig.Value.DefaultInboxes["kb"].Channels.ContainsKey "eru")

[<Fact>]
let ``InboxChannelAdd errors when channel already exists`` () =
    let state = newState ()
    let inbox = { makeInbox "/kb" with Channels = Map.ofList [ "eru", { Description = None; Agent = None } ] }
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", inbox ]; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let cmd : InboxChannelAdd.Command = { InboxName = "kb"; ChannelName = "eru"; Agent = None; Description = None; DryRun = false }
    InboxChannelAdd.execute deps cmd |> assertError

[<Fact>]
let ``InboxChannelList lists channels sorted by name`` () =
    let state = newState ()
    let inbox = { makeInbox "/kb" with Channels = Map.ofList [ "z-chan", { Description = None; Agent = None }; "a-chan", { Description = None; Agent = Some "ingestor" } ] }
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", inbox ]; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let rows = InboxChannelList.execute deps "kb" |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(2, rows.Length)
    Assert.Equal("a-chan", rows[0].Name)
    Assert.Equal("z-chan", rows[1].Name)

[<Fact>]
let ``InboxChannelRemove removes a channel`` () =
    let state = newState ()
    let inbox = { makeInbox "/kb" with Channels = Map.ofList [ "eru", { Description = None; Agent = None } ] }
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", inbox ]; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let cmd : InboxChannelRemove.Command = { InboxName = "kb"; ChannelName = "eru"; DryRun = false }
    InboxChannelRemove.execute deps cmd |> ignore
    Assert.True(state.WrittenLocalConfig.Value.Inboxes["kb"].Channels.IsEmpty)

[<Fact>]
let ``InboxChannelRemove errors when channel not found`` () =
    let state = newState ()
    let local = { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", makeInbox "/kb" ]; Settings = None }
    let deps = makeDeps None (Some local) [] state
    let cmd : InboxChannelRemove.Command = { InboxName = "kb"; ChannelName = "missing"; DryRun = false }
    InboxChannelRemove.execute deps cmd |> assertError
