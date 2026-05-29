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

    let private noPromptEnv =
        System.Action<System.Collections.Generic.IDictionary<string, string>>(fun env ->
            env["GIT_TERMINAL_PROMPT"] <- "0"
            env["GIT_ASKPASS"] <- "echo")

    let private runGit (verbose: bool) (args: string) (workingDirectory: string option) =
        if verbose then
            match workingDirectory with
            | Some wd -> Command.Run("git", args, workingDirectory = wd, configureEnvironment = noPromptEnv, noEcho = true)
            | None    -> Command.Run("git", args, configureEnvironment = noPromptEnv, noEcho = true)
        else
            match workingDirectory with
            | Some wd -> Command.ReadAsync("git", args, wd, noPromptEnv).Result |> ignore
            | None    -> Command.ReadAsync("git", args, configureEnvironment = noPromptEnv).Result |> ignore

    let fetchRemoteContent (verbose: bool) (url: string) (branch: string) (remotePath: string) : Result<(string * string) list, string> =
        withTempDir (fun tmpDir ->
            try
                runGit verbose $"clone --filter=blob:none --sparse --depth=1 {branchFlag branch}-- {url} {tmpDir}" None
                runGit verbose $"sparse-checkout set --no-cone {remotePath}" (Some tmpDir)
                let files =
                    Directory.EnumerateFiles(tmpDir, "*", SearchOption.AllDirectories)
                    |> Seq.filter (fun f ->
                        let rel = Path.GetRelativePath(tmpDir, f)
                        not (rel.StartsWith(".git")))
                    |> Seq.map (fun f ->
                        let rel = Path.GetRelativePath(tmpDir, f).Replace(Path.DirectorySeparatorChar, '/')
                        rel, File.ReadAllText f)
                    |> Seq.toList
                if files.IsEmpty then
                    Error $"'{remotePath}' not found in '{url}' on branch '{branch}'"
                else
                    Ok files
            with ex ->
                Error ex.Message)

    let checkRemoteAccess (url: string) : Result<unit, string> =
        try
            Command.ReadAsync("git", $"ls-remote {url} HEAD", configureEnvironment = noPromptEnv).Result |> ignore
            Ok ()
        with ex ->
            Error ex.Message

    let listRemoteTopLevel (verbose: bool) (url: string) (branch: string option) : Result<string list, string> =
        let bFlag = branch |> Option.map (fun b -> $"--branch {b} ") |> Option.defaultValue ""
        withTempDir (fun tmpDir ->
            try
                runGit verbose $"clone --filter=blob:none --depth=1 --no-checkout {bFlag}-- {url} {tmpDir}" None
                let struct (stdout, _) : struct (string * string) =
                    Command.ReadAsync("git", "ls-tree HEAD --name-only", tmpDir, noPromptEnv).Result
                let entries =
                    stdout.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                    |> List.map (fun s -> s.Trim())
                    |> List.filter (fun s -> s <> "")
                Ok entries
            with ex ->
                Error ex.Message)
