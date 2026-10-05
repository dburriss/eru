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

    let fetchRemoteContent (verbose: bool) (url: string) (branch: string) (remotePaths: string list) : Result<(string * string) list, string> =
        withTempDir (fun tmpDir ->
            try
                runGit verbose $"clone --filter=blob:none --sparse --depth=1 {branchFlag branch}-- {url} {tmpDir}" None
                let pathsArg = remotePaths |> String.concat " "
                runGit verbose $"sparse-checkout set --no-cone {pathsArg}" (Some tmpDir)
                let files =
                    Directory.EnumerateFiles(tmpDir, "*", SearchOption.AllDirectories)
                    |> Seq.filter (fun f ->
                        let rel = Path.GetRelativePath(tmpDir, f)
                        not (rel.StartsWith(".git")))
                    |> Seq.map (fun f ->
                        let rel = Path.GetRelativePath(tmpDir, f).Replace(Path.DirectorySeparatorChar, '/')
                        rel, File.ReadAllText f)
                    |> Seq.toList
                Ok files
            with ex ->
                Error ex.Message)

    let checkRemoteAccess (url: string) : Result<unit, string> =
        try
            Command.ReadAsync("git", $"ls-remote {url} HEAD", configureEnvironment = noPromptEnv).Result |> ignore
            Ok ()
        with ex ->
            Error ex.Message

    let getRemoteHeadSha (url: string) (branch: string option) : Result<string, string> =
        let refName = branch |> Option.defaultValue "HEAD"
        try
            let struct (stdout, _) : struct (string * string) =
                Command.ReadAsync("git", $"ls-remote {url} {refName}", configureEnvironment = noPromptEnv).Result
            let firstLine =
                stdout.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                |> Array.tryHead
            match firstLine |> Option.map (fun l -> l.Split('\t').[0].Trim()) with
            | Some sha when sha <> "" -> Ok sha
            | _ -> Error $"no ref '{refName}' found for {url}"
        with ex ->
            Error ex.Message

    let listRemoteTopLevel (verbose: bool) (url: string) (branch: string option) : Result<string list, string> =
        let bFlag = branch |> Option.map branchFlag |> Option.defaultValue ""
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

    let listRemoteFiles (verbose: bool) (url: string) (branch: string option) (basePath: string option) : Result<string list, string> =
        let bFlag = branch |> Option.map branchFlag |> Option.defaultValue ""
        withTempDir (fun tmpDir ->
            try
                runGit verbose $"clone --filter=blob:none --depth=1 --no-checkout {bFlag}-- {url} {tmpDir}" None
                let treeTarget = basePath |> Option.map (fun bp -> $"HEAD:{bp}") |> Option.defaultValue "HEAD"
                let struct (stdout, _) : struct (string * string) =
                    Command.ReadAsync("git", $"ls-tree -r --name-only {treeTarget}", tmpDir, noPromptEnv).Result
                let entries =
                    stdout.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                    |> List.map (fun s -> s.Trim())
                    |> List.filter (fun s -> s <> "")
                Ok entries
            with ex ->
                Error ex.Message)

    /// Move a file, creating the destination's parent directory. Uses `git mv` (a staged rename)
    /// when the source is tracked by git; falls back to a plain move when it is untracked or not in
    /// a git repository. A destination that already exists is an error either way.
    let moveFile (src: string) (dst: string) : Result<unit, string> =
        try
            let srcFull = Path.GetFullPath src
            let dstFull = Path.GetFullPath dst
            let dstDir = Path.GetDirectoryName dstFull
            if dstDir <> null && dstDir <> "" then Directory.CreateDirectory dstDir |> ignore
            let srcDir = Path.GetDirectoryName srcFull
            let viaGit =
                if File.Exists dstFull then Error "destination already exists"
                else
                    Forge.GitOps.moveFile srcDir (Path.GetFileName srcFull) (Path.GetRelativePath(srcDir, dstFull))
                    |> Async.RunSynchronously
            match viaGit with
            | Ok () -> Ok ()
            | Error _ ->
                File.Move(srcFull, dstFull)
                Ok ()
        with ex -> Error ex.Message
