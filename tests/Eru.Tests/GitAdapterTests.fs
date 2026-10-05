module Eru.Tests.GitAdapterTests

open System
open System.IO
open Xunit
open Eru.Adapters

// ── Git repo setup helpers ────────────────────────────────────────────────────

let private runGit (workingDir: string) (args: string) =
    let psi = Diagnostics.ProcessStartInfo("git", args)
    psi.WorkingDirectory <- workingDir
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    use p = Diagnostics.Process.Start(psi)
    let errTask = p.StandardError.ReadToEndAsync()
    p.StandardOutput.ReadToEnd() |> ignore
    p.WaitForExit()
    let err = errTask.Result
    if p.ExitCode <> 0 then
        failwithf "git %s failed: %s" args err

let private makeRepo (files: (string * string) list) : string =
    let dir = Path.Combine(Path.GetTempPath(), "eru-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore
    runGit dir "init -b main ."
    runGit dir "-c user.email=test@test.com -c user.name=Test config user.email test@test.com"
    runGit dir "-c user.email=test@test.com -c user.name=Test config user.name Test"
    for (path, content) in files do
        let fullPath = Path.Combine(dir, path)
        let fileDir = Path.GetDirectoryName(fullPath)
        if fileDir <> null && fileDir <> "" then
            Directory.CreateDirectory(fileDir) |> ignore
        File.WriteAllText(fullPath, content)
    runGit dir "add ."
    runGit dir "-c user.email=test@test.com -c user.name=Test commit -m init"
    dir

let private cleanup (dir: string) =
    try Directory.Delete(dir, true) with _ -> ()

// ── fetchRemoteContent tests ──────────────────────────────────────────────────

[<Fact>]
let ``fetchRemoteContent returns file content at root`` () =
    let dir = makeRepo [ ("hello.txt", "hello world") ]
    try
        let result = GitAdapter.fetchRemoteContent false $"file://{dir}" "main" ["hello.txt"]
        match result with
        | Error e -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok files ->
            let (_, content) = Assert.Single(files)
            Assert.Equal("hello world", content)
    finally
        cleanup dir

[<Fact>]
let ``fetchRemoteContent returns file content in subdirectory`` () =
    let dir = makeRepo [ ("sub/deep/file.md", "deep content") ]
    try
        let result = GitAdapter.fetchRemoteContent false $"file://{dir}" "main" ["sub/deep/file.md"]
        match result with
        | Error e -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok files ->
            let (_, content) = Assert.Single(files)
            Assert.Equal("deep content", content)
    finally
        cleanup dir

[<Fact>]
let ``fetchRemoteContent returns empty list for nonexistent file`` () =
    let dir = makeRepo [ ("exists.txt", "content") ]
    try
        let result = GitAdapter.fetchRemoteContent false $"file://{dir}" "main" ["no-such-file.txt"]
        match result with
        | Error e -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok files -> Assert.Empty(files)
    finally
        cleanup dir

[<Fact>]
let ``fetchRemoteContent returns Error for nonexistent branch`` () =
    let dir = makeRepo [ ("file.txt", "content") ]
    try
        let result = GitAdapter.fetchRemoteContent false $"file://{dir}" "no-such-branch" ["file.txt"]
        Assert.True(Result.isError result, "Expected Error for missing branch")
    finally
        cleanup dir

[<Fact>]
let ``fetchRemoteContent returns all files matching a glob pattern`` () =
    let dir = makeRepo [
        ("docs/a.md", "content-a")
        ("docs/b.md", "content-b")
        ("docs/other.txt", "content-txt")
        ("root.md", "root-content")
    ]
    try
        let result = GitAdapter.fetchRemoteContent false $"file://{dir}" "main" ["docs/*.md"]
        match result with
        | Error e -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok files ->
            Assert.Equal(2, files.Length)
            let paths = files |> List.map fst |> Set.ofList
            Assert.Contains("docs/a.md", paths)
            Assert.Contains("docs/b.md", paths)
            let contentA = files |> List.find (fun (p, _) -> p = "docs/a.md") |> snd
            Assert.Equal("content-a", contentA)
    finally
        cleanup dir

// ── listRemoteTopLevel tests ──────────────────────────────────────────────────

[<Fact>]
let ``listRemoteTopLevel returns top-level entry names only`` () =
    let dir = makeRepo [
        ("root.txt", "r")
        ("sub/nested.txt", "n")
        ("another.md", "a")
    ]
    try
        let result = GitAdapter.listRemoteTopLevel false $"file://{dir}" (Some "main")
        match result with
        | Error e      -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok entries ->
            Assert.Contains("root.txt", entries)
            Assert.Contains("another.md", entries)
            Assert.Contains("sub", entries)
            Assert.DoesNotContain("sub/nested.txt", entries)
    finally
        cleanup dir

[<Fact>]
let ``listRemoteTopLevel returns KNOWLEDGE directory when present`` () =
    let dir = makeRepo [
        ("KNOWLEDGE/guide.md", "guide")
        ("README.md", "readme")
    ]
    try
        let result = GitAdapter.listRemoteTopLevel false $"file://{dir}" (Some "main")
        match result with
        | Error e    -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok entries -> Assert.Contains("KNOWLEDGE", entries)
    finally
        cleanup dir

[<Fact>]
let ``listRemoteTopLevel uses HEAD when branch is None`` () =
    let dir = makeRepo [ ("file.txt", "content") ]
    try
        let result = GitAdapter.listRemoteTopLevel false $"file://{dir}" None
        match result with
        | Error e    -> Assert.Fail($"Expected Ok but got Error: {e}")
        | Ok entries -> Assert.Contains("file.txt", entries)
    finally
        cleanup dir

// ── RemoteInboxAdapter.push ───────────────────────────────────────────────────

[<Fact>]
let ``push commits new files to the default branch of a remote repo`` () =
    let remote = makeRepo [ ("README.md", "hi") ]
    // A non-bare repo refuses pushes to its checked-out branch.
    runGit remote "config receive.denyCurrentBranch updateInstead"
    try
        let result = RemoteInboxAdapter.push $"file://{remote}" None "inbox: add note" [ ("inbox/raw/default/a.md", "hello") ]
        Assert.Equal(Ok "main", result)
        Assert.Equal("hello", File.ReadAllText(Path.Combine(remote, "inbox/raw/default/a.md")))
    finally cleanup remote

[<Fact>]
let ``push refuses to overwrite an existing file`` () =
    let remote = makeRepo [ ("inbox/raw/default/a.md", "old") ]
    runGit remote "config receive.denyCurrentBranch updateInstead"
    try
        match RemoteInboxAdapter.push $"file://{remote}" None "msg" [ ("inbox/raw/default/a.md", "new") ] with
        | Error _ -> Assert.Equal("old", File.ReadAllText(Path.Combine(remote, "inbox/raw/default/a.md")))
        | Ok _ -> Assert.Fail "expected an error"
    finally cleanup remote

[<Fact>]
let ``push creates a new branch but refuses an existing non-default one`` () =
    let remote = makeRepo [ ("README.md", "hi") ]
    runGit remote "branch existing"
    try
        Assert.Equal(Ok "fresh", RemoteInboxAdapter.push $"file://{remote}" (Some "fresh") "msg" [ ("inbox/raw/default/b.md", "x") ])
        match RemoteInboxAdapter.push $"file://{remote}" (Some "existing") "msg" [ ("inbox/raw/default/c.md", "x") ] with
        | Error _ -> ()
        | Ok _ -> Assert.Fail "expected an error"
    finally cleanup remote

// ── moveFile ──────────────────────────────────────────────────────────────────

let private gitOut (dir: string) (args: string) =
    let psi = Diagnostics.ProcessStartInfo("git", args)
    psi.WorkingDirectory <- dir
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    use p = Diagnostics.Process.Start(psi)
    let out = p.StandardOutput.ReadToEnd()
    p.WaitForExit()
    out

[<Fact>]
let ``moveFile stages a rename for a tracked file`` () =
    let repo = makeRepo [ ("inbox/raw/default/a.md", "hello") ]
    try
        let src = Path.Combine(repo, "inbox/raw/default/a.md")
        let dst = Path.Combine(repo, "inbox/archive/default/a.md")
        Assert.Equal(Ok (), GitAdapter.moveFile src dst)
        Assert.False(File.Exists src)
        Assert.Equal("hello", File.ReadAllText dst)
        Assert.StartsWith("R", (gitOut repo "status --porcelain").TrimStart())
    finally cleanup repo

[<Fact>]
let ``moveFile falls back to a plain move for an untracked file`` () =
    let repo = makeRepo [ ("README.md", "hi") ]
    try
        let src = Path.Combine(repo, "inbox/raw/default/b.md")
        Directory.CreateDirectory(Path.GetDirectoryName src) |> ignore
        File.WriteAllText(src, "x")
        let dst = Path.Combine(repo, "inbox/archive/default/b.md")
        Assert.Equal(Ok (), GitAdapter.moveFile src dst)
        Assert.False(File.Exists src)
        Assert.Equal("x", File.ReadAllText dst)
    finally cleanup repo

[<Fact>]
let ``moveFile works outside a git repository`` () =
    let dir = Path.Combine(Path.GetTempPath(), "eru-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try
        let src = Path.Combine(dir, "raw/c.md")
        Directory.CreateDirectory(Path.GetDirectoryName src) |> ignore
        File.WriteAllText(src, "y")
        let dst = Path.Combine(dir, "archive/c.md")
        Assert.Equal(Ok (), GitAdapter.moveFile src dst)
        Assert.Equal("y", File.ReadAllText dst)
    finally cleanup dir

[<Fact>]
let ``moveFile errors and keeps the source when the destination exists`` () =
    let repo = makeRepo [ ("raw/a.md", "new"); ("archive/a.md", "old") ]
    try
        let src = Path.Combine(repo, "raw/a.md")
        match GitAdapter.moveFile src (Path.Combine(repo, "archive/a.md")) with
        | Error _ ->
            Assert.True(File.Exists src)
            Assert.Equal("old", File.ReadAllText(Path.Combine(repo, "archive/a.md")))
        | Ok _ -> Assert.Fail "expected an error"
    finally cleanup repo
