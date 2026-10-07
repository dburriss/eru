module Eru.Site.IndexBuilder

open System.IO
open Eru

let private toSlug (remotePath: string) =
    remotePath.Replace('/', '_').Replace('\\', '_').Replace(' ', '-')

let private fileTitle (remotePath: string) = Path.GetFileName remotePath

let private fileExtension (remotePath: string) = Path.GetExtension remotePath

let private isGlob (path: string) = path.Contains('*') || path.Contains('?') || path.Contains('[')

let private determineStatus (entry: IndexEntry) : FileStatus =
    match entry.LocalPath, entry.CacheRelPath with
    | Some _, _  -> Pulled
    | _, Some _  -> Cached
    | None, None -> IndexOnly

let private pageUrl (sourceName: string) (remotePath: string) (status: FileStatus) (ext: string) : string option =
    match status, ext with
    | (Pulled | Cached), ".md" ->
        let slug = toSlug remotePath
        Some $"files/{sourceName}/{slug}.html"
    | _ -> None

let bundleDisplayName (sourceName: string) (bundlePath: string) : string =
    let p = bundlePath.Trim('/')
    if p = "" then sourceName else $"{sourceName}/{p}"

let buildModel (deps: Deps) (cfg: EffectiveConfig) : Result<SiteModel, string> =
    let sources =
        cfg.Sources
        |> List.choose (fun src ->
            match deps.ReadSourceIndex src.Name with
            | Error _ | Ok None -> None
            | Ok (Some index) ->
                let cachedManifest =
                    match deps.ReadCachedManifest src.Name with
                    | Ok (Some m) -> Some m
                    | _           -> None

                let manifestDescription = cachedManifest |> Option.bind (fun m -> m.Description)

                let docs =
                    index.Entries
                    |> Map.toList
                    |> List.filter (fun (remotePath, _) -> not (isGlob remotePath))
                    |> List.filter (fun (remotePath, _) -> not (Patterns.matchesAny cfg.SiteIgnorePatterns remotePath))
                    |> List.map (fun (remotePath, entry) ->
                        let status = determineStatus entry
                        let ext    = fileExtension remotePath
                        let body =
                            match status, entry.CacheRelPath with
                            | (Pulled | Cached), Some relPath ->
                                match deps.ReadCachedSourceContent src.Name relPath with
                                | Ok (Some content) ->
                                    let trimmed = content.TrimStart()
                                    let len = min 500 trimmed.Length
                                    Some trimmed.[..len - 1]
                                | _ -> None
                            | _ -> None
                        {
                            Id          = $"{src.Name}:{remotePath}"
                            Source      = src.Name
                            RemotePath  = remotePath
                            Title       = entry.Title |> Option.defaultValue (fileTitle remotePath)
                            Extension   = ext
                            Tags        = entry.Tags
                            Description = entry.Description
                            SyncStatus  = status
                            Body        = body
                            PageUrl     = pageUrl src.Name remotePath status ext
                            Type        = entry.Type
                            Status      = entry.OkfStatus
                            Generated   = entry.Generated
                            Verified    = entry.Verified
                            StaleAfter  = entry.StaleAfter
                            Resource    = entry.Resource
                            Bundle      =
                                Bundle.mostSpecificBundle src.Bundles remotePath
                                |> Option.map (fun b -> bundleDisplayName src.Name b.Path)
                        })

                Some {
                    Name        = src.Name
                    Url         = src.Url
                    Description = manifestDescription
                    HasManifest = cachedManifest.IsSome
                    FileCount   = docs.Length
                    Files       = docs
                })

    let allDocs = sources |> List.collect (fun s -> s.Files)

    let tags =
        allDocs
        |> List.collect (fun d -> d.Tags)
        |> List.distinct
        |> List.sort
        |> List.map (fun tag ->
            let files = allDocs |> List.filter (fun d -> List.contains tag d.Tags)
            { SiteTag.Name = tag; FileCount = files.Length; Files = files })

    let types =
        allDocs
        |> List.choose (fun d -> d.Type)
        |> List.distinct
        |> List.sort
        |> List.map (fun t ->
            let files = allDocs |> List.filter (fun d -> d.Type = Some t)
            { SiteType.Name = t; FileCount = files.Length; Files = files })

    let bundles =
        cfg.Sources
        |> List.collect (fun src ->
            src.Bundles
            |> List.map (fun b ->
                let name  = bundleDisplayName src.Name b.Path
                let files = allDocs |> List.filter (fun d -> d.Bundle = Some name)
                { SiteBundle.Name      = name
                  Source    = src.Name
                  Path      = b.Path.Trim('/')
                  Kind      = (match b.Kind with Okf -> "okf" | Manifest -> "manifest")
                  FileCount = files.Length
                  Files     = files }))
        |> List.filter (fun b -> not cfg.SiteHideEmptyBundles || b.FileCount > 0)
        |> List.sortBy (fun b -> b.Name)

    let extensions =
        allDocs
        |> List.map (fun d -> d.Extension)
        |> List.filter (fun e -> e <> "")
        |> List.distinct
        |> List.sort

    Ok {
        Documents     = allDocs
        Sources       = sources
        Tags          = tags
        Types         = types
        Bundles       = bundles
        AllExtensions = extensions
    }
