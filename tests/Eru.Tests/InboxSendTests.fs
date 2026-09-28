module Eru.Tests.InboxSendTests

open Xunit
open Eru

// ── Test helpers ─────────────────────────────────────────────────────────────

type private CapturedState = {
    mutable WrittenFiles : (string * string) list
}

let private newState () : CapturedState = { WrittenFiles = [] }

let private fixedNow = System.DateTimeOffset(2026, 9, 27, 10, 0, 0, System.TimeSpan.Zero)

let private makeDeps
    (globalCfg: GlobalConfig option)
    (localCfg: LocalConfig option)
    (existingDirs: string list)
    (existingFiles: Map<string, string>)
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
        WriteLocalFile     = fun path content -> state.WrittenFiles <- state.WrittenFiles @ [ (path, content) ]; Ok ()
        ReadLocalFile      = fun path ->
            match Map.tryFind path existingFiles with
            | Some c -> Ok (Some c)
            | None   -> Ok None
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
        GetUtcNow               = fun () -> fixedNow
        ListLocalFiles          = fun _ -> Ok []
        ListLocalDirectories = fun _ -> Ok []
        MoveLocalFile           = fun _ _ -> Ok ()
        RunAgent                = fun _ _ _ -> Ok ""
    }

let private makeInbox path : InboxConfig =
    { Path = path; RawPath = None; DefaultChannel = None; Channels = Map.empty }

let private singleInboxLocal (name: string) (inbox: InboxConfig) : LocalConfig option =
    Some { Version = 1; Sources = []; Collections = []; Inboxes = Map.ofList [ name, inbox ]; Settings = None }

let private emptyCmd : InboxSend.Command =
    { Content = None; InboxName = None; Channel = None; Title = None; Note = None; As = None; DryRun = false }

let private assertError (result: Result<'a, string>) = match result with Ok _ -> Assert.Fail "Expected Error result" | Error _ -> ()

// ── content-type classification ─────────────────────────────────────────────

[<Fact>]
let ``send classifies an http(s) string as a url capture`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "https://example.com/article" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("url", result.Kind)

[<Fact>]
let ``send classifies an existing local file as a file capture`` () =
    let state = newState ()
    let files = Map.ofList [ "notes.md", "hello world" ]
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] files state
    let cmd = { emptyCmd with Content = Some "notes.md" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("file", result.Kind)
    Assert.True(result.SidecarPath.IsSome)

[<Fact>]
let ``send classifies plain text with no matching file as a message capture`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "just a note to self" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("message", result.Kind)
    Assert.True(result.SidecarPath.IsNone)

[<Fact>]
let ``send --as message forces message classification even if content matches a file`` () =
    let state = newState ()
    let files = Map.ofList [ "notes.md", "hello world" ]
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] files state
    let cmd = { emptyCmd with Content = Some "notes.md"; As = Some "message" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("message", result.Kind)

[<Fact>]
let ``send --as file errors when the path does not exist`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "notes.md"; As = Some "file" }
    InboxSend.execute deps cmd |> assertError

[<Fact>]
let ``send errors on an unknown --as value`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "hi"; As = Some "bogus" }
    InboxSend.execute deps cmd |> assertError

[<Fact>]
let ``send errors when content is empty or missing`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    InboxSend.execute deps emptyCmd |> assertError
    InboxSend.execute deps { emptyCmd with Content = Some "   " } |> assertError

// ── slug derivation ──────────────────────────────────────────────────────────

[<Fact>]
let ``send derives a slug from the first words of a message`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "Ripgrep --hidden still respects .gitignore by default" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Contains("ripgrep-hidden-still-respects", result.TargetPath)

[<Fact>]
let ``send falls back to 'note' slug when message has no alphanumeric content`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "!!!" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Contains("note.md", result.TargetPath)

[<Fact>]
let ``send derives a slug from the last URL path segment`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "https://example.com/blog/my-great-post" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Contains("my-great-post.md", result.TargetPath)

[<Fact>]
let ``send --title overrides the derived slug`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "some message"; Title = Some "custom-slug" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Contains("custom-slug.md", result.TargetPath)

// ── filename collision ───────────────────────────────────────────────────────

[<Fact>]
let ``send appends -2 when the derived filename already exists`` () =
    let state = newState ()
    let existingPath = "/kb/inbox/raw/default/2026-09-27T100000-hello.md"
    let files = Map.ofList [ existingPath, "already here" ]
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] files state
    let cmd = { emptyCmd with Content = Some "hello"; Title = Some "hello" }
    let result = InboxSend.execute deps cmd |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("/kb/inbox/raw/default/2026-09-27T100000-hello-2.md", result.TargetPath)

// ── capture shape ────────────────────────────────────────────────────────────

[<Fact>]
let ``send writes message capture with type raw and null resource frontmatter`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "hello there"; Title = Some "hello" }
    InboxSend.execute deps cmd |> ignore
    let _, body = state.WrittenFiles |> List.head
    Assert.StartsWith("---\ntype: raw\nresource: null\ngenerated:\n  by: eru inbox send\n  at: 2026-09-27T10:00:00Z\n---\n\nhello there", body)

[<Fact>]
let ``send writes url capture with resource set to the url and note appended`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "https://example.com/x"; Note = Some "why this matters" }
    InboxSend.execute deps cmd |> ignore
    let _, body = state.WrittenFiles |> List.head
    Assert.Contains("resource: https://example.com/x", body)
    Assert.EndsWith("https://example.com/x\n\nwhy this matters", body)

[<Fact>]
let ``send file capture copies content verbatim and writes a minimal sidecar`` () =
    let state = newState ()
    let files = Map.ofList [ "notes.md", "# Title\n\nverbatim content" ]
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] files state
    let cmd = { emptyCmd with Content = Some "notes.md" }
    InboxSend.execute deps cmd |> ignore
    Assert.Equal(2, state.WrittenFiles.Length)
    let (mainPath, mainBody) = state.WrittenFiles[0]
    let (sidecarPath, sidecarBody) = state.WrittenFiles[1]
    Assert.Equal("# Title\n\nverbatim content", mainBody)
    Assert.Equal(mainPath + ".meta.json", sidecarPath)
    Assert.Equal("""{"captured_at": "2026-09-27T10:00:00Z", "original_url": null}""", sidecarBody)

[<Fact>]
let ``send dryrun does not write any files`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    let cmd = { emptyCmd with Content = Some "hello"; DryRun = true }
    InboxSend.execute deps cmd |> ignore
    Assert.Empty state.WrittenFiles

// ── inbox resolution & errors ────────────────────────────────────────────────

[<Fact>]
let ``send errors when no inbox is configured`` () =
    let state = newState ()
    let deps = makeDeps None (Some { Version = 1; Sources = []; Collections = []; Inboxes = Map.empty; Settings = None }) [] Map.empty state
    InboxSend.execute deps { emptyCmd with Content = Some "hi" } |> assertError

[<Fact>]
let ``send errors listing names when multiple inboxes are configured and none specified`` () =
    let state = newState ()
    let local = {
        Version = 1; Sources = []; Collections = []
        Inboxes = Map.ofList [ "kb", makeInbox "/kb"; "other", makeInbox "/other" ]
        Settings = None
    }
    let deps = makeDeps None (Some local) [ "/kb"; "/other" ] Map.empty state
    match InboxSend.execute deps { emptyCmd with Content = Some "hi" } with
    | Ok _ -> Assert.Fail "expected error"
    | Error e ->
        Assert.Contains("kb", e)
        Assert.Contains("other", e)

[<Fact>]
let ``send uses the explicitly named inbox when multiple are configured`` () =
    let state = newState ()
    let local = {
        Version = 1; Sources = []; Collections = []
        Inboxes = Map.ofList [ "kb", makeInbox "/kb"; "other", makeInbox "/other" ]
        Settings = None
    }
    let deps = makeDeps None (Some local) [ "/kb"; "/other" ] Map.empty state
    let result = InboxSend.execute deps { emptyCmd with Content = Some "hi"; InboxName = Some "other" } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("other", result.InboxName)

[<Fact>]
let ``send errors when the named inbox is not configured`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [ "/kb" ] Map.empty state
    InboxSend.execute deps { emptyCmd with Content = Some "hi"; InboxName = Some "missing" } |> assertError

[<Fact>]
let ``send errors when the inbox's path does not exist on disk`` () =
    let state = newState ()
    let deps = makeDeps None (singleInboxLocal "kb" (makeInbox "/kb")) [] Map.empty state
    InboxSend.execute deps { emptyCmd with Content = Some "hi" } |> assertError

[<Fact>]
let ``send uses the inbox's channel and default channel resolution`` () =
    let state = newState ()
    let inbox = { makeInbox "/kb" with DefaultChannel = Some "eru" }
    let deps = makeDeps None (singleInboxLocal "kb" inbox) [ "/kb" ] Map.empty state
    let result = InboxSend.execute deps { emptyCmd with Content = Some "hi" } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("eru", result.Channel)
    Assert.Contains("/inbox/raw/eru/", result.TargetPath)

[<Fact>]
let ``send -c overrides the inbox's default channel`` () =
    let state = newState ()
    let inbox = { makeInbox "/kb" with DefaultChannel = Some "eru" }
    let deps = makeDeps None (singleInboxLocal "kb" inbox) [ "/kb" ] Map.empty state
    let result = InboxSend.execute deps { emptyCmd with Content = Some "hi"; Channel = Some "explicit" } |> function Ok r -> r | Error e -> failwith e
    Assert.Equal("explicit", result.Channel)
