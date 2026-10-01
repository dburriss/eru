namespace Eru.Adapters

open System
open System.IO
open Forge

/// Pushes inbox items to a remote git repo using FsForge (shallow bare clone -> worktree -> commit -> push).
module RemoteInboxAdapter =

    let private authFor (url: string) : GitAuth =
        if url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)
        then GitHub.githubAuth ""   // ambient `gh` authentication
        else NoAuth                 // ssh / other hosts: rely on whatever git is already configured with

    let private run (a: Async<Result<'a, string>>) : Result<'a, string> =
        try Async.RunSynchronously a with ex -> Error ex.Message

    let private resolveBranch (auth: GitAuth) (url: string) (basePath: string) (requested: string option) : Result<string, string> =
        match run (GitOps.getDefaultBranch basePath), requested with
        | Error e, _ -> Error e
        | Ok def, None -> Ok def
        | Ok def, Some b when b = def -> Ok b
        | Ok _, Some b ->
            // The push is a force-push of a branch cut from the default branch, so an existing
            // non-default branch would be overwritten. Only allow creating a new one.
            match run (GitOps.lsRemoteHeads auth url b) with
            | Error e -> Error e
            | Ok true -> Error $"branch '{b}' already exists on {url}; use the default branch or a new branch name."
            | Ok false -> Ok b

    let private writeFiles (worktree: string) (files: (string * string) list) : Result<unit, string> =
        try
            let full = Path.GetFullPath worktree
            let targets =
                files |> List.map (fun (rel, content) ->
                    let target = Path.GetFullPath(Path.Combine(full, rel))
                    if not (target.StartsWith(full + string Path.DirectorySeparatorChar)) then
                        failwith $"refusing to write outside the repository: {rel}"
                    if File.Exists target then failwith $"'{rel}' already exists in the remote inbox; try again."
                    target, content)
            for (target, content) in targets do
                Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
                File.WriteAllText(target, content)
            Ok ()
        with ex -> Error ex.Message

    /// Returns the branch pushed to.
    let push (url: string) (branch: string option) (message: string) (files: (string * string) list) : Result<string, string> =
        let root = Path.Combine(Path.GetTempPath(), "eru-inbox-" + Guid.NewGuid().ToString("N"))
        let basePath = Path.Combine(root, "repo.git")
        let worktree = Path.Combine(root, "wt")
        let auth = authFor url
        try
            match run (GitOps.ensureClone auth url basePath) with
            | Error e -> Error e
            | Ok _ ->
            match resolveBranch auth url basePath branch with
            | Error e -> Error e
            | Ok target ->
            match run (GitOps.getWorktree basePath worktree ("eru-inbox-" + Guid.NewGuid().ToString("N").Substring(0, 8))) with
            | Error e -> Error e
            | Ok wt ->
            match writeFiles wt files with
            | Error e -> Error e
            | Ok () ->
            match run (GitOps.commitAll wt message) with
            | Error "no-diff" -> Error "nothing to commit."
            | Error e -> Error e
            | Ok () ->
            match run (GitOps.pushBranch auth "origin" wt target) with
            | Error e -> Error e
            | Ok () -> Ok target
        finally
            try GitOps.cleanupAll root |> ignore with _ -> ()
