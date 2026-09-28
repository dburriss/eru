module Eru.Tests.InboxProcessTests

open Xunit
open Eru

// ── Test helpers ─────────────────────────────────────────────────────────────

type private CapturedState = {
    mutable Moves         : (string * string) list
    mutable RunAgentCalls : (AgentConfig * string * string) list
}

let private newState () : CapturedState = { Moves = []; RunAgentCalls = [] }

let private fixedNow = System.DateTimeOffset(2026, 9, 27, 10, 0, 0, System.TimeSpan.Zero)

let private acpAgent (command: string) : AgentConfig = { Protocol = "acp"; Command = command; Args = [ "acp" ]; InstructionsPath = None; Timeout = None }

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (filesByDir: Map<string, string list>)
    (fileContents: Map<string, string>)
    (runAgent: AgentConfig -> string -> string -> Result<string, string>)
    (state: CapturedState) : Deps =
    {
        ReadGlobalConfig   = fun () -> Ok globalCfg
        ReadLocalConfig    = fun () -> Ok localCfg
        WriteLocalConfig   = fun _ -> Ok ()
        WriteGlobalConfig  = fun _ -> Ok ()
        ReadLockEntries    = fun _ -> Ok []
        WriteLockEntries   = fun _ _ -> Ok ()
        FetchRemoteContent = fun _ _ paths -> Ok (paths |> List.map (fun p -> (p, $"content:{p}")))
        ListRemoteTopLevel = fun _ _ -> Ok []
        ListRemoteFiles    = fun _ _ _ -> Ok []
        WriteLocalFile     = fun _ _ -> Ok ()
        ReadLocalFile      = fun path -> Ok (Map.tryFind path fileContents)
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
        DirectoryExists         = fun _ -> true
        GetUtcNow               = fun () -> fixedNow
        ListLocalFiles          = fun dir ->
            match Map.tryFind dir filesByDir with
            | Some names -> Ok (names |> List.map (fun n -> System.IO.Path.Combine(dir, n)))
            | None       -> Ok []
        ListLocalDirectories    = fun dir ->
            let prefix = dir.TrimEnd('/') + "/"
            filesByDir
            |> Map.toList
            |> List.map fst
            |> List.filter (fun p -> p.StartsWith prefix && not (p.Substring(prefix.Length).Contains "/"))
            |> Ok
        MoveLocalFile           = fun src dst -> state.Moves <- state.Moves @ [ (src, dst) ]; Ok ()
        RunAgent                = fun agent wd prompt onChunk ->
            state.RunAgentCalls <- state.RunAgentCalls @ [ (agent, wd, prompt) ]
            let result = runAgent agent wd prompt
            // Simulate streaming: a real agent reports its response one fragment at a
            // time via onChunk as it works, so a single "final text" chunk here is enough
            // to exercise a test's onChunk wiring without modelling a real ACP stream.
            result |> Result.iter onChunk
            result
    }

let private okAgent : AgentConfig -> string -> string -> Result<string, string> = fun _ _ _ -> Ok "curated"

let private makeInbox (channels: Map<string, InboxChannelConfig>) : InboxConfig =
    { Path = "/kb"; RawPath = None; DefaultChannel = None; Channels = channels }

let private singleInboxLocal (channels: Map<string, InboxChannelConfig>) : LocalConfig option =
    Some { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ "kb", makeInbox channels ]; Settings = None }

let private emptyOpts : InboxProcess.Options =
    { InboxName = None; Channel = None; ItemName = None; All = false; DryRun = false }

let private assertError (result: Result<'a, string>) = match result with Ok _ -> Assert.Fail "Expected Error result" | Error _ -> ()

// ── Selection ────────────────────────────────────────────────────────────────

[<Fact>]
let ``process picks the oldest item pooled across agent-enabled channels, excluding channels without an agent`` () =
    let state = newState ()
    let channels =
        Map.ofList [
            "eru",       { Description = None; Agent = Some (acpAgent "opencode") }
            "other",     { Description = None; Agent = Some (acpAgent "opencode") }
            "no-agent",  { Description = None; Agent = None }
        ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",      [ "2026-09-27T101500-a.md" ]
            "/kb/inbox/raw/other",    [ "2026-09-27T090000-b.md" ]
            "/kb/inbox/raw/no-agent", [ "2026-09-27T080000-c.md" ]
        ]
    let contents = Map.ofList [ "/kb/inbox/raw/other/2026-09-27T090000-b.md", "body-b" ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    let result = InboxProcess.execute deps { emptyOpts with DryRun = true } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(1, result.Length)
    Assert.Equal("other", result.[0].Channel)
    Assert.Equal("/kb/inbox/raw/other/2026-09-27T090000-b.md", result.[0].ItemPath)

[<Fact>]
let ``-c restricts scope to one channel even if others have items`` () =
    let state = newState ()
    let channels =
        Map.ofList [
            "eru",   { Description = None; Agent = Some (acpAgent "opencode") }
            "other", { Description = None; Agent = Some (acpAgent "opencode") }
        ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",   [ "2026-09-27T101500-a.md" ]
            "/kb/inbox/raw/other", [ "2026-09-27T090000-b.md" ]
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.execute deps { emptyOpts with Channel = Some "eru"; DryRun = true } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(1, result.Length)
    Assert.Equal("eru", result.[0].Channel)

[<Fact>]
let ``-c on a channel with no agent errors`` () =
    let state = newState ()
    let channels = Map.ofList [ "no-agent", { Description = None; Agent = None } ]
    let deps = makeDeps None (singleInboxLocal channels) Map.empty Map.empty okAgent state
    InboxProcess.execute deps { emptyOpts with Channel = Some "no-agent" } |> assertError

[<Fact>]
let ``process ignores dotfiles like .gitkeep sitting in the raw channel directory`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ ".gitkeep" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.execute deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Empty(result)
    Assert.Empty(state.RunAgentCalls)
    Assert.Empty(state.Moves)

[<Fact>]
let ``pendingWithoutAgent does not count dotfiles like .gitkeep as pending items`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/default", [ ".gitkeep" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.pendingWithoutAgent deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Empty(result)

// ── <name> selection ─────────────────────────────────────────────────────────

[<Fact>]
let ``<name> selects a specific item by full filename or stem across scope`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-ripgrep-tips.md"; "2026-09-27T090000-other.md" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state

    let byFullName = InboxProcess.execute deps { emptyOpts with ItemName = Some "2026-09-27T101500-ripgrep-tips.md"; DryRun = true } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(1, byFullName.Length)

    let byStem = InboxProcess.execute deps { emptyOpts with ItemName = Some "2026-09-27T101500-ripgrep-tips"; DryRun = true } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(1, byStem.Length)
    Assert.Equal(byFullName.[0].ItemPath, byStem.[0].ItemPath)

[<Fact>]
let ``<name> ambiguous across two channels in scope errors asking for -c`` () =
    let state = newState ()
    let channels =
        Map.ofList [
            "eru",   { Description = None; Agent = Some (acpAgent "opencode") }
            "other", { Description = None; Agent = Some (acpAgent "opencode") }
        ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",   [ "2026-09-27T101500-tips.md" ]
            "/kb/inbox/raw/other", [ "2026-09-27T101500-tips.md" ]
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    InboxProcess.execute deps { emptyOpts with ItemName = Some "2026-09-27T101500-tips" } |> assertError

[<Fact>]
let ``<name> with no match anywhere in scope errors`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let deps = makeDeps None (singleInboxLocal channels) Map.empty Map.empty okAgent state
    InboxProcess.execute deps { emptyOpts with ItemName = Some "missing" } |> assertError

// ── --all ────────────────────────────────────────────────────────────────────

[<Fact>]
let ``--all processes every pending item in pooled chronological order across channels`` () =
    let state = newState ()
    let channels =
        Map.ofList [
            "eru",   { Description = None; Agent = Some (acpAgent "opencode") }
            "other", { Description = None; Agent = Some (acpAgent "opencode") }
        ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",   [ "2026-09-27T101500-a.md" ]
            "/kb/inbox/raw/other", [ "2026-09-27T090000-b.md" ]
        ]
    let contents =
        Map.ofList [
            "/kb/inbox/raw/eru/2026-09-27T101500-a.md",   "content-a"
            "/kb/inbox/raw/other/2026-09-27T090000-b.md", "content-b"
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    let result = InboxProcess.execute deps { emptyOpts with All = true } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(2, result.Length)
    Assert.Equal("other", result.[0].Channel)
    Assert.Equal("eru", result.[1].Channel)

[<Fact>]
let ``executeWithProgress reports each item's 1-based index/total/filename via onItemStart`` () =
    let state = newState ()
    let channels =
        Map.ofList [
            "eru",   { Description = None; Agent = Some (acpAgent "opencode") }
            "other", { Description = None; Agent = Some (acpAgent "opencode") }
        ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",   [ "2026-09-27T101500-a.md" ]
            "/kb/inbox/raw/other", [ "2026-09-27T090000-b.md" ]
        ]
    let contents =
        Map.ofList [
            "/kb/inbox/raw/eru/2026-09-27T101500-a.md",   "content-a"
            "/kb/inbox/raw/other/2026-09-27T090000-b.md", "content-b"
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    let calls = System.Collections.Generic.List<int * int * string>()
    InboxProcess.executeWithProgress deps { emptyOpts with All = true } (fun idx total name -> calls.Add(idx, total, name)) (fun _ -> ())
    |> function Ok r -> r | Error e -> failwith e
    |> ignore
    Assert.Equal<(int * int * string) list>(
        [ (1, 2, "2026-09-27T090000-b.md"); (2, 2, "2026-09-27T101500-a.md") ],
        List.ofSeq calls)

[<Fact>]
let ``executeWithProgress streams each item's agent response through onChunk`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents = Map.ofList [ "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "content-a" ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    let chunks = System.Collections.Generic.List<string>()
    InboxProcess.executeWithProgress deps emptyOpts (fun _ _ _ -> ()) chunks.Add
    |> function Ok r -> r | Error e -> failwith e
    |> ignore
    Assert.Equal<string list>([ "curated" ], List.ofSeq chunks)

[<Fact>]
let ``--all stops at first failure and reports partial progress`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",
            [ "2026-09-27T080000-a.md"; "2026-09-27T090000-b.md"; "2026-09-27T100000-c.md" ]
        ]
    let failOnB : AgentConfig -> string -> string -> Result<string, string> =
        fun _ _ prompt -> if prompt.Contains "b-content" then Error "agent boom" else Ok "curated"
    let contents =
        Map.ofList [
            "/kb/inbox/raw/eru/2026-09-27T080000-a.md", "a-content"
            "/kb/inbox/raw/eru/2026-09-27T090000-b.md", "b-content"
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents failOnB state
    match InboxProcess.execute deps { emptyOpts with All = true } with
    | Ok _ -> Assert.Fail "expected an error"
    | Error msg ->
        Assert.Contains("1", msg)
        Assert.Contains("agent boom", msg)
    Assert.Equal(1, state.Moves.Length)

// ── Agent instructions prepending ───────────────────────────────────────────

[<Fact>]
let ``prompt prepends the default .agents/agents/ingestor.md instructions when present`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents =
        Map.ofList [
            "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "we need a documentation bundle"
            "/kb/.agents/agents/ingestor.md", "# Ingestor\nCurate raw items into structured notes."
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    InboxProcess.execute deps emptyOpts |> ignore
    let (_, _, prompt) = List.exactlyOne state.RunAgentCalls
    Assert.Contains("Curate raw items into structured notes.", prompt)
    Assert.Contains("we need a documentation bundle", prompt)

[<Fact>]
let ``prompt is just the item content when no instructions file exists anywhere`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents = Map.ofList [ "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "we need a documentation bundle" ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    InboxProcess.execute deps emptyOpts |> ignore
    let (_, _, prompt) = List.exactlyOne state.RunAgentCalls
    Assert.Equal("we need a documentation bundle", prompt)

[<Fact>]
let ``prompt prepends an explicitly-configured InstructionsPath instead of the default`` () =
    let state = newState ()
    let agent = { acpAgent "opencode" with InstructionsPath = Some "custom/curator.md" }
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some agent } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents =
        Map.ofList [
            "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "we need a documentation bundle"
            "/kb/custom/curator.md", "Custom curation instructions."
            "/kb/.agents/agents/ingestor.md", "Should not be used."
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    InboxProcess.execute deps emptyOpts |> ignore
    let (_, _, prompt) = List.exactlyOne state.RunAgentCalls
    Assert.Contains("Custom curation instructions.", prompt)
    Assert.DoesNotContain("Should not be used.", prompt)

[<Fact>]
let ``an explicit InstructionsPath that doesn't resolve is a clear error`` () =
    let state = newState ()
    let agent = { acpAgent "opencode" with InstructionsPath = Some "missing.md" }
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some agent } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents = Map.ofList [ "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "we need a documentation bundle" ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    match InboxProcess.execute deps emptyOpts with
    | Ok _ -> Assert.Fail "expected an error"
    | Error msg -> Assert.Contains("missing.md", msg)
    Assert.Empty(state.RunAgentCalls)

// ── Archiving ────────────────────────────────────────────────────────────────

[<Fact>]
let ``success moves item and its sidecar from raw to archive under that item's own channel`` () =
    let state = newState ()
    let channels =
        Map.ofList [
            "eru",   { Description = None; Agent = Some (acpAgent "opencode") }
            "other", { Description = None; Agent = Some (acpAgent "opencode") }
        ]
    let filesByDir =
        Map.ofList [
            "/kb/inbox/raw/eru",   [ "2026-09-27T101500-a.md" ]
            "/kb/inbox/raw/other", [ "2026-09-27T090000-b.png"; "2026-09-27T090000-b.png.meta.json" ]
        ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.execute deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(1, result.Length)
    Assert.Equal("other", result.[0].Channel)
    Assert.Equal("/kb/inbox/archive/other/2026-09-27T090000-b.png", result.[0].ArchivePath)
    Assert.Contains(("/kb/inbox/raw/other/2026-09-27T090000-b.png", "/kb/inbox/archive/other/2026-09-27T090000-b.png"), state.Moves)
    Assert.Contains(("/kb/inbox/raw/other/2026-09-27T090000-b.png.meta.json", "/kb/inbox/archive/other/2026-09-27T090000-b.png.meta.json"), state.Moves)

[<Fact>]
let ``archiving treats an already-agent-archived item as success instead of erroring`` () =
    // Simulates an agent (given real curation instructions, e.g. ingestor.md) that archives
    // and commits the raw item itself as part of curating it, before eru's own move runs —
    // the source is already gone, but the destination is already exactly where it should be.
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents = Map.ofList [ "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "content-a" ]
    let baseDeps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    let deps =
        { baseDeps with
            MoveLocalFile = fun _ _ -> Error "source file not found"
            ReadLocalFile = fun path ->
                if path = "/kb/inbox/archive/eru/2026-09-27T101500-a.md" then Ok (Some "curated elsewhere")
                else Map.tryFind path contents |> Ok }
    let result = InboxProcess.execute deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Equal(1, result.Length)
    Assert.Equal("/kb/inbox/archive/eru/2026-09-27T101500-a.md", result.[0].ArchivePath)

[<Fact>]
let ``a genuine move failure (destination not already present) still errors`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents = Map.ofList [ "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "content-a" ]
    let baseDeps = makeDeps None (singleInboxLocal channels) filesByDir contents okAgent state
    let deps = { baseDeps with MoveLocalFile = fun _ _ -> Error "disk full" }
    match InboxProcess.execute deps emptyOpts with
    | Ok _ -> Assert.Fail "expected an error"
    | Error msg -> Assert.Contains("disk full", msg)

[<Fact>]
let ``RunAgent returning Error leaves the item in place and surfaces the message`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let contents = Map.ofList [ "/kb/inbox/raw/eru/2026-09-27T101500-a.md", "content-a" ]
    let failing : AgentConfig -> string -> string -> Result<string, string> = fun _ _ _ -> Error "agent unreachable"
    let deps = makeDeps None (singleInboxLocal channels) filesByDir contents failing state
    match InboxProcess.execute deps emptyOpts with
    | Ok _ -> Assert.Fail "expected an error"
    | Error msg -> Assert.Contains("agent unreachable", msg)
    Assert.Empty(state.Moves)

// ── Scope errors ─────────────────────────────────────────────────────────────

[<Fact>]
let ``no channel in scope has an agent configured errors clearly, nothing invoked`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = None } ]
    let deps = makeDeps None (singleInboxLocal channels) Map.empty Map.empty okAgent state
    InboxProcess.execute deps emptyOpts |> assertError
    Assert.Empty(state.RunAgentCalls)

[<Fact>]
let ``nothing pending anywhere in scope returns an empty, non-error result`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let deps = makeDeps None (singleInboxLocal channels) Map.empty Map.empty okAgent state
    let result = InboxProcess.execute deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Empty(result)

// ── pendingWithoutAgent diagnostic ──────────────────────────────────────────

[<Fact>]
let ``pendingWithoutAgent reports items sitting in an unregistered raw channel directory`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    // "default" was never registered via channel add at all — only its raw/ dir exists on disk,
    // exactly like an `inbox send` with no -c falling back to it.
    let filesByDir = Map.ofList [ "/kb/inbox/raw/default", [ "2026-09-27T080000-a.md"; "2026-09-27T090000-b.md" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.pendingWithoutAgent deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Equal<(string * int) list>([ "default", 2 ], result)

[<Fact>]
let ``pendingWithoutAgent excludes channels that already have an agent configured`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.pendingWithoutAgent deps emptyOpts |> function Ok r -> r | Error e -> failwith e
    Assert.Empty(result)

[<Fact>]
let ``pendingWithoutAgent is a no-op when -c was given explicitly`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/default", [ "2026-09-27T080000-a.md" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    let result = InboxProcess.pendingWithoutAgent deps { emptyOpts with Channel = Some "eru" } |> function Ok r -> r | Error e -> failwith e
    Assert.Empty(result)

// ── --dryrun ─────────────────────────────────────────────────────────────────

[<Fact>]
let ``--dryrun never calls RunAgent or MoveLocalFile`` () =
    let state = newState ()
    let channels = Map.ofList [ "eru", { Description = None; Agent = Some (acpAgent "opencode") } ]
    let filesByDir = Map.ofList [ "/kb/inbox/raw/eru", [ "2026-09-27T101500-a.md" ] ]
    let deps = makeDeps None (singleInboxLocal channels) filesByDir Map.empty okAgent state
    InboxProcess.execute deps { emptyOpts with DryRun = true } |> ignore
    Assert.Empty(state.RunAgentCalls)
    Assert.Empty(state.Moves)

// ── Frontmatter.body ─────────────────────────────────────────────────────────

[<Fact>]
let ``Frontmatter.body strips a present frontmatter block`` () =
    let content = "---\ntype: raw\nresource: null\n---\n\nHello world"
    Assert.Equal("Hello world", Frontmatter.body content)

[<Fact>]
let ``Frontmatter.body returns content unchanged when there is no block`` () =
    let content = "Just a plain message, no frontmatter."
    Assert.Equal(content, Frontmatter.body content)
