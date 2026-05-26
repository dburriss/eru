namespace Eru.Adapters

open System
open System.IO
open SimpleExec

module GitAdapter =

    let private withTempDir (f: string -> Result<'a, string>) : Result<'a, string> =
        let tmpDir = Path.Combine(Path.GetTempPath(), "eru-" + Guid.NewGuid().ToString("N"))
        try
            f tmpDir
        finally
            try
                if Directory.Exists tmpDir then
                    Directory.Delete(tmpDir, true)
            with _ -> ()

    let private branchFlag (branch: string) =
        if branch = "HEAD" then "" else $"--branch {branch} "

    let fetchRemoteContent (url: string) (branch: string) (remotePath: string) : Result<string, string> =
        withTempDir (fun tmpDir ->
            try
                Command.Run(
                    "git",
                    $"clone --filter=blob:none --sparse --depth=1 {branchFlag branch}-- {url} {tmpDir}",
                    noEcho = true)
                Command.Run(
                    "git",
                    $"sparse-checkout set --no-cone {remotePath}",
                    workingDirectory = tmpDir,
                    noEcho = true)
                let filePath = Path.Combine(tmpDir, remotePath.Replace('/', Path.DirectorySeparatorChar))
                if File.Exists filePath then
                    Ok (File.ReadAllText filePath)
                else
                    Error $"'{remotePath}' not found in '{url}' on branch '{branch}'"
            with ex ->
                Error ex.Message)

    let listRemoteTopLevel (url: string) (branch: string option) : Result<string list, string> =
        let bFlag = branch |> Option.map (fun b -> $"--branch {b} ") |> Option.defaultValue ""
        withTempDir (fun tmpDir ->
            try
                Command.Run(
                    "git",
                    $"clone --filter=blob:none --depth=1 --no-checkout {bFlag}-- {url} {tmpDir}",
                    noEcho = true)
                let struct (stdout, _) : struct (string * string) =
                    Command.ReadAsync("git", "ls-tree HEAD --name-only", tmpDir).Result
                let entries =
                    stdout.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                    |> List.map (fun s -> s.Trim())
                    |> List.filter (fun s -> s <> "")
                Ok entries
            with ex ->
                Error ex.Message)
