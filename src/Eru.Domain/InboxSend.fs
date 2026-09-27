namespace Eru

open System
open System.IO
open System.Text.RegularExpressions

module InboxSend =

    type Command = {
        Content   : string option
        InboxName : string option
        Channel   : string option
        Title     : string option
        Note      : string option
        As        : string option
        DryRun    : bool
    }

    type SendResult = {
        InboxName   : string
        Channel     : string
        Kind        : string   // "message" | "url" | "file"
        TargetPath  : string
        SidecarPath : string option
    }

    type private ContentKind =
        | Message of string
        | Url of string
        | ExternalFile of path: string * content: string

    let private isUrl (s: string) =
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

    let private classify (deps: Deps) (asOverride: string option) (content: string) : Result<ContentKind, string> =
        match asOverride with
        | Some "message" -> Ok (Message content)
        | Some "url"     -> Ok (Url content)
        | Some "file"    ->
            match deps.ReadLocalFile content with
            | Ok (Some c) -> Ok (ExternalFile (content, c))
            | Ok None     -> Error $"'{content}' is not an existing local file."
            | Error e     -> Error e
        | Some other -> Error $"unknown --as value '{other}' — expected 'message', 'file', or 'url'."
        | None ->
            if isUrl content then Ok (Url content)
            else
                match deps.ReadLocalFile content with
                | Ok (Some c) -> Ok (ExternalFile (content, c))
                | Ok None     -> Ok (Message content)
                | Error e     -> Error e

    let private slugify (maxLen: int) (s: string) : string =
        let lowered = s.ToLowerInvariant()
        let dashed = Regex.Replace(lowered, "[^a-z0-9]+", "-")
        let collapsed = Regex.Replace(dashed, "-{2,}", "-").Trim('-')
        if collapsed.Length > maxLen then collapsed.Substring(0, maxLen).Trim('-') else collapsed

    let private slugFromMessage (text: string) : string =
        let words = text.Split([| ' '; '\t'; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries) |> Array.truncate 6 |> String.concat " "
        match slugify 60 words with
        | "" -> "note"
        | s  -> s

    let private slugFromUrl (url: string) : string =
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri ->
            let segs = uri.AbsolutePath.Split('/') |> Array.filter (fun s -> s <> "")
            match segs |> Array.tryLast with
            | Some last ->
                match slugify 60 (Path.GetFileNameWithoutExtension last) with
                | "" -> slugify 60 uri.Host
                | s  -> s
            | None -> slugify 60 uri.Host
        | false, _ -> slugify 60 url

    let private slugFromFile (path: string) : string =
        match slugify 60 (Path.GetFileNameWithoutExtension path) with
        | "" -> "file"
        | s  -> s

    let private timestamp (now: DateTimeOffset) : string =
        now.ToString("yyyy-MM-ddTHHmmss")

    let private renderFrontmatter (resource: string option) (now: DateTimeOffset) : string =
        let resourceLine = match resource with Some r -> $"resource: {r}" | None -> "resource: null"
        let atStr = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
        $"---\ntype: raw\n{resourceLine}\ngenerated:\n  by: eru inbox send\n  at: {atStr}\n---\n\n"

    let rec private uniquePath (deps: Deps) (dir: string) (stem: string) (ext: string) (attempt: int) : string =
        let candidateStem = if attempt = 1 then stem else $"{stem}-{attempt}"
        let path = Path.Combine(dir, candidateStem + ext)
        match deps.ReadLocalFile path with
        | Ok (Some _) -> uniquePath deps dir stem ext (attempt + 1)
        | _ -> path

    let private resolveInbox (eff: EffectiveConfig) (explicit_: string option) : Result<string * InboxConfig, string> =
        let byName name =
            match Map.tryFind name eff.Inboxes with
            | Some inbox -> Ok (name, inbox)
            | None       -> Error $"inbox '{name}' not configured."
        match explicit_ with
        | Some name -> byName name
        | None ->
            match eff.DefaultInbox with
            | Some name -> byName name
            | None ->
                match eff.Inboxes |> Map.toList with
                | [ single ] -> Ok single
                | []         -> Error "no inbox configured. Run 'eru inbox add <name> <path>' first."
                | many       ->
                    let names = many |> List.map fst |> String.concat ", "
                    Error $"multiple inboxes configured ({names}) — specify one with -i/--inbox or set settings.defaultInbox."

    let execute (deps: Deps) (cmd: Command) : Result<SendResult, string> =
        match cmd.Content with
        | None -> Error "specify a message, file path, or URL to send."
        | Some content when content.Trim() = "" -> Error "specify a message, file path, or URL to send."
        | Some content ->

        match deps.ReadGlobalConfig (), deps.ReadLocalConfig () with
        | Error e, _ | _, Error e -> Error e
        | Ok globalCfg, Ok localCfg ->

        match Config.merge globalCfg localCfg with
        | Error e -> Error e
        | Ok eff ->

        match resolveInbox eff cmd.InboxName with
        | Error e -> Error e
        | Ok (inboxName, inbox) ->

        if not (deps.DirectoryExists inbox.Path) then
            Error $"inbox '{inboxName}''s path '{inbox.Path}' does not exist on disk."
        else

        match classify deps cmd.As content with
        | Error e -> Error e
        | Ok contentKind ->

        let channel   = cmd.Channel |> Option.orElse inbox.DefaultChannel |> Option.defaultValue "default"
        let rawPath   = inbox.RawPath |> Option.defaultValue "inbox/raw"
        let targetDir = Path.Combine(inbox.Path, rawPath, channel)
        let now       = deps.GetUtcNow ()
        let ts        = timestamp now

        let write (path: string) (content: string) =
            if cmd.DryRun then Ok () else deps.WriteLocalFile path content

        match contentKind with
        | Message text ->
            let slug = cmd.Title |> Option.defaultWith (fun () -> slugFromMessage text)
            let path = uniquePath deps targetDir $"{ts}-{slug}" ".md" 1
            let noteSuffix = cmd.Note |> Option.map (fun n -> "\n\n" + n) |> Option.defaultValue ""
            let body = renderFrontmatter None now + text + noteSuffix
            write path body
            |> Result.map (fun () -> { InboxName = inboxName; Channel = channel; Kind = "message"; TargetPath = path; SidecarPath = None })
        | Url url ->
            let slug = cmd.Title |> Option.defaultWith (fun () -> slugFromUrl url)
            let path = uniquePath deps targetDir $"{ts}-{slug}" ".md" 1
            let noteSuffix = cmd.Note |> Option.map (fun n -> "\n\n" + n) |> Option.defaultValue ""
            let body = renderFrontmatter (Some url) now + url + noteSuffix
            write path body
            |> Result.map (fun () -> { InboxName = inboxName; Channel = channel; Kind = "url"; TargetPath = path; SidecarPath = None })
        | ExternalFile (srcPath, fileContent) ->
            let ext = Path.GetExtension srcPath
            let slug = cmd.Title |> Option.defaultWith (fun () -> slugFromFile srcPath)
            let path = uniquePath deps targetDir $"{ts}-{slug}" ext 1
            let sidecarPath = path + ".meta.json"
            let atStr = now.ToString("yyyy-MM-ddTHH:mm:ssZ")
            let sidecar = $$"""{"captured_at": "{{atStr}}", "original_url": null}"""
            match write path fileContent with
            | Error e -> Error e
            | Ok () ->
                write sidecarPath sidecar
                |> Result.map (fun () -> { InboxName = inboxName; Channel = channel; Kind = "file"; TargetPath = path; SidecarPath = Some sidecarPath })
