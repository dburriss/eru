namespace Eru

module Sync =

    type Options = { DryRun: bool }

    type SyncStatus =
        | Current
        | Drifted   // "would update" on dry-run; "updated" on actual run
        | Missing
        | Skipped of string
        | Blocked

    type SyncEntry = {
        Status    : SyncStatus
        LocalPath : string
    }

    type SyncResult = {
        Entries : SyncEntry list
        DryRun  : bool
    }

    type private EntryResult =
        | ECurrent of LockEntry
        | EDrifted of LockEntry * string
        | EMissing of LockEntry
        | ESkipped of LockEntry * string
        | EBlocked of LockEntry

    let private classifyEntry
        (deps: Deps)
        (sources: SourceConfig list)
        (blockPatterns: string list)
        (allowPatterns: string list)
        (allowBinaries: bool)
        (entry: LockEntry) : EntryResult =
        if Patterns.isPathBlocked blockPatterns allowPatterns entry.RemotePath then
            EBlocked entry
        else
        match sources |> List.tryFind (fun s -> s.Name = entry.SourceName) with
        | None -> ESkipped (entry, $"source '{entry.SourceName}' not configured")
        | Some source ->
            match source.Url with
            | None -> ESkipped (entry, $"source '{entry.SourceName}' has no URL")
            | Some url ->
                let branch = source.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch entry.RemotePath with
                | Error _ -> EMissing entry
                | Ok [] -> EMissing entry
                | Ok ((_, content) :: _) ->
                    if Patterns.isBlocked blockPatterns allowPatterns allowBinaries entry.RemotePath content then
                        EBlocked entry
                    else
                        let hash = deps.HashContent content
                        if hash = entry.ContentHash then ECurrent entry
                        else EDrifted (entry, content)

    let private toSyncEntry (r: EntryResult) : SyncEntry =
        match r with
        | ECurrent e       -> { Status = Current;      LocalPath = e.LocalPath }
        | EDrifted (e, _)  -> { Status = Drifted;      LocalPath = e.LocalPath }
        | EMissing e       -> { Status = Missing;      LocalPath = e.LocalPath }
        | ESkipped (e, rs) -> { Status = Skipped rs;   LocalPath = e.LocalPath }
        | EBlocked e       -> { Status = Blocked;      LocalPath = e.LocalPath }

    let execute (deps: Deps) (opts: Options) : Result<SyncResult, string> =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        for src in eff.Sources do
            match src.Url with
            | None -> ()
            | Some url ->
                let branch = src.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch ".eru/manifest.json" with
                | Error _            -> ()
                | Ok []              -> ()
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest src.Name raw |> ignore

        let eff = Config.withManifests deps.ReadCachedManifest eff

        match deps.ReadLockEntries eff.StateFile with
        | Error e -> Error $"Error reading lock file: {e}"
        | Ok entries ->

        let classified = entries |> List.map (classifyEntry deps eff.Sources eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries)

        if opts.DryRun then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = true }
        else

        let drifted = classified |> List.choose (function EDrifted (e, c) -> Some (e, c) | _ -> None)

        if drifted.IsEmpty then
            Ok { Entries = classified |> List.map toSyncEntry; DryRun = false }
        else

        let writeError =
            drifted |> List.tryPick (fun (entry, content) ->
                match deps.WriteLocalFile entry.LocalPath content with
                | Error e -> Some e
                | Ok ()   -> None)

        match writeError with
        | Some e -> Error $"Error writing file: {e}"
        | None ->

        let updatedEntries =
            entries |> List.map (fun entry ->
                match drifted |> List.tryFind (fun (e, _) -> e.LocalPath = entry.LocalPath) with
                | Some (_, content) -> { entry with ContentHash = deps.HashContent content }
                | None              -> entry)

        match deps.WriteLockEntries eff.StateFile updatedEntries with
        | Error e -> Error $"Error writing lock file: {e}"
        | Ok () -> Ok { Entries = classified |> List.map toSyncEntry; DryRun = false }
