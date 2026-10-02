module Eru.Tests.DomainPathConventionTests

open System
open System.IO
open System.Text.RegularExpressions
open Xunit

// Domain paths are '/'-separated; System.IO.Path emits '\' on Windows and breaks
// CI there. See "Key conventions" in AGENTS.md. Files below handle real OS paths
// or only read a file name, where Path.* is fine.
let private allowed =
    set [ "PathUtil.fs"; "Init.fs"; "Add.fs"; "OkfVerify.fs"; "Patterns.fs"; "Frontmatter.fs" ]

let private forbidden = Regex(@"\bPath\.(Combine|GetDirectoryName|GetFileName)\b(?!WithoutExtension)", RegexOptions.Compiled)

let private domainDir () =
    let rec up (dir: DirectoryInfo) =
        if isNull dir then failwith "eru.slnx not found above test binary"
        elif File.Exists(Path.Combine(dir.FullName, "eru.slnx")) then Path.Combine(dir.FullName, "src", "Eru.Domain")
        else up dir.Parent
    up (DirectoryInfo AppContext.BaseDirectory)

[<Fact>]
let ``domain code does not use System.IO.Path to build or split paths`` () =
    let offenders =
        Directory.GetFiles(domainDir (), "*.fs")
        |> Array.filter (fun f -> not (allowed.Contains(Path.GetFileName f)))
        |> Array.collect (fun f ->
            File.ReadAllLines f
            |> Array.mapi (fun i line -> i + 1, line)
            |> Array.filter (fun (_, line) -> forbidden.IsMatch line && not (line.TrimStart().StartsWith "//"))
            |> Array.map (fun (n, line) -> $"{Path.GetFileName f}:{n}: {line.Trim()}"))
    Assert.True(
        offenders.Length = 0,
        "Use PathJoin.Combine / PathUtil.dirName / PathUtil.fileName in Eru.Domain (see AGENTS.md):\n"
        + String.Join("\n", offenders))
