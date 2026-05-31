namespace Eru

module Remove =

    type Command = {
        Target : string
        DryRun : bool
    }

    let private resolveEntry (target: string) (entries: LockEntry list) : Result<LockEntry, string> =
        let byHash = entries |> List.filter (fun e -> (Patterns.pathShortHash e.RemotePath).StartsWith target)
        let byPath = entries |> List.filter (fun e -> e.LocalPath = target)
        let matches = (byHash @ byPath) |> List.distinctBy (fun e -> e.LocalPath)
        match matches with
        | []  -> Error $"'{target}' did not match any tracked file."
        | [e] -> Ok e
        | _   -> Error $"'{target}' matched {matches.Length} files, be more specific."

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->
        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->
        match deps.ReadLockEntries eff.StateFile with
        | Error e -> Error $"Error reading lock file: {e}"
        | Ok entries ->
        match resolveEntry cmd.Target entries with
        | Error e -> Error e
        | Ok entry ->
        if cmd.DryRun then
            Ok $"Would remove '{entry.LocalPath}' from lock and delete file."
        else
            let remaining = entries |> List.filter (fun e -> e.LocalPath <> entry.LocalPath)
            match deps.WriteLockEntries eff.StateFile remaining with
            | Error e -> Error e
            | Ok () ->
            let fullPath = System.IO.Path.Combine(deps.GetCwd(), entry.LocalPath)
            match deps.DeleteLocalFile fullPath with
            | Error e -> Error $"Lock entry removed but could not delete file: {e}"
            | Ok () -> Ok $"Removed '{entry.LocalPath}'."
