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
