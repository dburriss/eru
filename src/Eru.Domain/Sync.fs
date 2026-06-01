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
                match deps.FetchRemoteContent url branch [".eru/manifest.json"] with
                | Error _            -> ()
                | Ok []              -> ()
                | Ok ((_, raw) :: _) -> deps.CacheSourceManifest src.Name raw |> ignore

        let eff = Config.withManifests deps.ReadCachedManifest eff

        match deps.ReadLockEntries eff.StateFile with
        | Error e -> Error $"Error reading lock file: {e}"
        | Ok entries ->

        // Batch fetch: one clone per source instead of one per entry
        let contentBySource : Map<string, Map<string, string>> =
            entries
            |> List.groupBy (fun e -> e.SourceName)
            |> List.choose (fun (sourceName, sourceEntries) ->
                match eff.Sources |> List.tryFind (fun s -> s.Name = sourceName) with
                | None -> None
                | Some src ->
                    match src.Url with
                    | None -> None
                    | Some url ->
                        let branch = src.Branch |> Option.defaultValue "HEAD"
                        let remotePaths = sourceEntries |> List.map (fun e -> e.RemotePath)
                        match deps.FetchRemoteContent url branch remotePaths with
                        | Error _  -> Some (sourceName, Map.empty)
                        | Ok files -> Some (sourceName, files |> Map.ofList))
            |> Map.ofList

        let classified =
            entries |> List.map (fun entry ->
                if Patterns.isPathBlocked eff.BlockPatterns eff.AllowPatterns entry.RemotePath then
                    EBlocked entry
                else
                match eff.Sources |> List.tryFind (fun s -> s.Name = entry.SourceName) with
                | None -> ESkipped (entry, $"source '{entry.SourceName}' not configured")
                | Some source ->
                    match source.Url with
                    | None -> ESkipped (entry, $"source '{entry.SourceName}' has no URL")
                    | Some _ ->
                        let contentMap = contentBySource |> Map.tryFind entry.SourceName |> Option.defaultValue Map.empty
                        match Map.tryFind entry.RemotePath contentMap with
                        | None -> EMissing entry
                        | Some content ->
                            if Patterns.isBlocked eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries entry.RemotePath content then
                                EBlocked entry
                            else
                                let hash = deps.HashContent content
                                if hash = entry.ContentHash then ECurrent entry
                                else EDrifted (entry, content))

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
