namespace Eru

module Sync =

    type Options = { DryRun: bool }

    type private EntryResult =
        | Current of LockEntry
        | Drifted of LockEntry * string   // entry + new content
        | Missing of LockEntry
        | Skipped of LockEntry * string   // entry + reason
        | Blocked of LockEntry            // entry matches a block pattern

    let private classifyEntry
        (deps: Deps)
        (sources: SourceConfig list)
        (blockPatterns: string list)
        (allowPatterns: string list)
        (allowBinaries: bool)
        (entry: LockEntry) : EntryResult =
        // Fast path: path-only check before fetching
        if Patterns.isPathBlocked blockPatterns allowPatterns entry.RemotePath then
            Blocked entry
        else
        match sources |> List.tryFind (fun s -> s.Name = entry.SourceName) with
        | None -> Skipped (entry, $"source '{entry.SourceName}' not configured")
        | Some source ->
            match source.Url with
            | None -> Skipped (entry, $"source '{entry.SourceName}' has no URL")
            | Some url ->
                let branch = source.Branch |> Option.defaultValue "HEAD"
                match deps.FetchRemoteContent url branch entry.RemotePath with
                | Error _ -> Missing entry
                | Ok [] -> Missing entry
                | Ok ((_, content) :: _) ->
                    if Patterns.isBlocked blockPatterns allowPatterns allowBinaries entry.RemotePath content then
                        Blocked entry
                    else
                        let hash = deps.HashContent content
                        if hash = entry.ContentHash then Current entry
                        else Drifted (entry, content)

    let run (deps: Deps) (opts: Options) : int =
        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e ->
            eprintfn "Error: %s" e
            1
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e ->
            eprintfn "Error: %s" e
            1
        | Ok eff ->

        // Refresh manifests for all sources (best-effort, silent on missing)
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
        | Error e ->
            eprintfn "Error reading lock file: %s" e
            1
        | Ok entries ->

        let results = entries |> List.map (classifyEntry deps eff.Sources eff.BlockPatterns eff.AllowPatterns eff.AllowBinaries)

        let label isDryRun result =
            match result with
            | Current _         -> "current"
            | Drifted _         -> if isDryRun then "drifted" else "updated"
            | Missing _         -> "missing"
            | Skipped _         -> "skipped"
            | Blocked _         -> "blocked"

        let entryPath = function
            | Current e | Missing e | Blocked e -> e.LocalPath
            | Drifted (e, _) | Skipped (e, _)   -> e.LocalPath

        for r in results do
            match r with
            | Skipped (e, reason) -> printfn "[%s]  %s  (%s)" (label opts.DryRun r) e.LocalPath reason
            | _                   -> printfn "[%s]  %s" (label opts.DryRun r) (entryPath r)

        let nCurrent = results |> List.sumBy (function Current _ -> 1 | _ -> 0)
        let nDrifted = results |> List.sumBy (function Drifted _ -> 1 | _ -> 0)
        let nMissing = results |> List.sumBy (function Missing _ -> 1 | _ -> 0)
        let nSkipped = results |> List.sumBy (function Skipped _ -> 1 | _ -> 0)
        let nBlocked = results |> List.sumBy (function Blocked _ -> 1 | _ -> 0)

        if opts.DryRun then
            printfn "Sync dry-run: %d drifted, %d current, %d missing, %d skipped, %d blocked."
                nDrifted nCurrent nMissing nSkipped nBlocked
            0
        else

        let drifted = results |> List.choose (function Drifted (e, c) -> Some (e, c) | _ -> None)

        if drifted.IsEmpty then
            printfn "Sync complete: 0 updated, %d current, %d missing, %d skipped, %d blocked."
                nCurrent nMissing nSkipped nBlocked
            0
        else

        let writeError =
            drifted |> List.tryPick (fun (entry, content) ->
                match deps.WriteLocalFile entry.LocalPath content with
                | Error e -> Some e
                | Ok ()   -> None)

        match writeError with
        | Some e ->
            eprintfn "Error writing file: %s" e
            1
        | None ->

        let updatedEntries =
            entries |> List.map (fun entry ->
                match drifted |> List.tryFind (fun (e, _) -> e.LocalPath = entry.LocalPath) with
                | Some (_, content) -> { entry with ContentHash = deps.HashContent content }
                | None              -> entry)

        match deps.WriteLockEntries eff.StateFile updatedEntries with
        | Error e ->
            eprintfn "Error writing lock file: %s" e
            1
        | Ok () ->
            printfn "Sync complete: %d updated, %d current, %d missing, %d skipped, %d blocked."
                nDrifted nCurrent nMissing nSkipped nBlocked
            0
