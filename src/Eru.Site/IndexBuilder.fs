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

let buildModel (deps: Deps) (cfg: EffectiveConfig) : Result<SiteModel, string> =
    let sources =
        cfg.Sources
        |> List.choose (fun src ->
            match deps.ReadSourceIndex src.Name with
            | Error _ | Ok None -> None
            | Ok (Some index) ->
                let hasManifest =
                    match deps.ReadCachedManifest src.Name with
                    | Ok (Some _) -> true
                    | _ -> false

                let docs =
                    index
                    |> Map.toList
                    |> List.filter (fun (remotePath, _) -> not (isGlob remotePath))
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
                            Title       = fileTitle remotePath
                            Extension   = ext
                            Tags        = entry.Tags
                            Description = entry.Description
                            Status      = status
                            Body        = body
                            PageUrl     = pageUrl src.Name remotePath status ext
                        })

                Some {
                    Name        = src.Name
                    Url         = src.Url
                    HasManifest = hasManifest
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
            { Name = tag; FileCount = files.Length; Files = files })

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
        AllExtensions = extensions
    }
