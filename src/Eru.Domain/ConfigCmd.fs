namespace Eru

open System.Text.Json
open System.Text.Json.Nodes

type ConfigScope = LocalScope | GlobalScope

// Everything the `config` command needs from the outside world. Kept separate from `Deps`
// so the command works on the raw file text: it must see the on-disk version, which the
// already-migrated configs behind `Deps.ReadGlobalConfig`/`ReadLocalConfig` hide.
type ConfigIO = {
    Path            : ConfigScope -> string
    ReadRaw         : string -> Result<string option, string>
    WriteRaw        : string -> string -> Result<unit, string>
    ParseGlobal     : string -> Result<GlobalConfig, string>   // reads any supported version, migrating to current
    ParseLocal      : string -> Result<LocalConfig, string>
    SerializeGlobal : GlobalConfig -> string
    SerializeLocal  : LocalConfig -> string
}

module ConfigCmd =

    type FileRow = {
        Scope          : string
        Path           : string
        Exists         : bool
        Version        : int option
        NeedsMigration : bool
        Problem        : string option
    }

    type Origin = FromLocal | FromGlobal | FromDefault

    type SettingRow = { Key: string; Value: string; Origin: string }

    type EntityRow = { Kind: string; Name: string; Origin: string }

    type View = { Settings: SettingRow list; Entities: EntityRow list }

    type MigrateOutcome =
        | AlreadyCurrent of version: int
        | Migrated of fromVersion: int * path: string * backup: string option * content: string

    let private scopeName = function LocalScope -> "local" | GlobalScope -> "global"

    let private originName = function FromLocal -> "local" | FromGlobal -> "global" | FromDefault -> "default"

    // A file with no "version" property is treated as the oldest shape.
    let private onDiskVersion (raw: string) : Result<int, string> =
        Config.probe raw |> Result.map (fun (v, _) -> defaultArg v 1)

    let list (io: ConfigIO) : FileRow list =
        [ GlobalScope; LocalScope ]
        |> List.map (fun scope ->
            let path = io.Path scope
            let row = { Scope = scopeName scope; Path = path; Exists = false; Version = None; NeedsMigration = false; Problem = None }
            match io.ReadRaw path with
            | Error e -> { row with Problem = Some e }
            | Ok None -> row
            | Ok (Some raw) ->
                match onDiskVersion raw with
                | Error e -> { row with Exists = true; Problem = Some e }
                | Ok v -> { row with Exists = true; Version = Some v; NeedsMigration = v < Config.supportedVersion })

    // ── normalized JSON view of each file ───────────────────────────────────────
    // Parse (migrating if needed) then re-serialize, so key paths are always the current
    // shape regardless of the file's on-disk version. Null means "not set".

    let private parseNode (text: string) : Result<JsonNode option, string> =
        try Ok (Option.ofObj (JsonNode.Parse text))
        with ex -> Error ex.Message

    let private normalizedNode (io: ConfigIO) (scope: ConfigScope) : Result<JsonNode option, string> =
        match io.ReadRaw (io.Path scope) with
        | Error e -> Error e
        | Ok None -> Ok None
        | Ok (Some raw) ->
            let normalized =
                match scope with
                | GlobalScope -> io.ParseGlobal raw |> Result.map io.SerializeGlobal
                | LocalScope  -> io.ParseLocal raw  |> Result.map io.SerializeLocal
            normalized
            |> Result.mapError (fun e -> $"{scopeName scope} config ({io.Path scope}): {e}")
            |> Result.bind parseNode

    let private walk (root: JsonNode) (path: string list) : JsonNode option =
        path
        |> List.fold (fun (node: JsonNode option) seg ->
            match node with
            | Some (:? JsonObject as o) -> if o.ContainsKey seg then Option.ofObj o[seg] else None
            | _ -> None) (Some root)

    let private render (node: JsonNode) : string = node.ToJsonString()

    // Settings whose effective value is "local, else global, else built-in default".
    // Mirrors the `first local global` fields in `Config.merge`.
    let private defaultOf (value: obj) : JsonNode = JsonSerializer.SerializeToNode value

    let private knownSettings : (string * JsonNode option) list = [
        "sync.commitOnPull",              Some (defaultOf false)
        "sync.stateFile",                 Some (defaultOf "eru.lock")
        "sync.branch",                    None
        "mcp.refreshIntervalMinutes",     Some (defaultOf 60)
        "files.blockPatterns",            Some (defaultOf Config.defaultBlockPatterns)
        "files.allowPatterns",            Some (defaultOf Config.defaultAllowPatterns)
        "files.allowBinaries",            Some (defaultOf Config.defaultAllowBinaries)
        "okf.ignorePatterns",             Some (defaultOf Config.defaultOkfIgnorePatterns)
        "site.ignorePatterns",            Some (defaultOf Config.defaultSiteIgnorePatterns)
        "site.hideEmptyBundles",          Some (defaultOf Config.defaultSiteHideEmptyBundles)
        "inbox.default",                  None
        "inbox.watchIntervalSeconds",     Some (defaultOf 30)
    ]

    let private splitKey (key: string) : string list =
        key.Split('.', System.StringSplitOptions.RemoveEmptyEntries ||| System.StringSplitOptions.TrimEntries) |> Array.toList

    let private resolve (local: JsonNode option) (global': JsonNode option) (key: string) : (JsonNode * Origin) option =
        let path = splitKey key
        let from origin node = node |> Option.bind (fun n -> walk n path) |> Option.map (fun v -> v, origin)
        from FromLocal local |> Option.orElse (from FromGlobal global')

    let private nodes (io: ConfigIO) =
        normalizedNode io LocalScope
        |> Result.bind (fun l -> normalizedNode io GlobalScope |> Result.map (fun g -> l, g))

    let get (io: ConfigIO) (key: string) : Result<SettingRow, string> =
        if splitKey key |> List.isEmpty then Error "key must not be empty"
        else
        nodes io
        |> Result.bind (fun (l, g) ->
            match resolve l g key with
            | Some (v, origin) -> Ok { Key = key; Value = render v; Origin = originName origin }
            | None ->
                match knownSettings |> List.tryFind (fun (k, _) -> k = key) with
                | Some (_, Some d) -> Ok { Key = key; Value = render d; Origin = originName FromDefault }
                | Some (_, None)   -> Ok { Key = key; Value = "null"; Origin = originName FromDefault }
                | None             -> Error $"'{key}' is not set (and has no built-in default)")

    let view (io: ConfigIO) : Result<View, string> =
        nodes io
        |> Result.bind (fun (l, g) ->
            let parsedGlobal = io.ReadRaw (io.Path GlobalScope) |> Result.bind (function Some r -> io.ParseGlobal r |> Result.map Some | None -> Ok None)
            let parsedLocal  = io.ReadRaw (io.Path LocalScope)  |> Result.bind (function Some r -> io.ParseLocal r  |> Result.map Some | None -> Ok None)
            match parsedGlobal, parsedLocal with
            | Error e, _ | _, Error e -> Error e
            | Ok pg, Ok pl ->
                Config.merge pg pl
                |> Result.map (fun eff ->
                    let settings =
                        knownSettings
                        |> List.map (fun (key, def) ->
                            match resolve l g key with
                            | Some (v, o) -> { Key = key; Value = render v; Origin = originName o }
                            | None ->
                                let v = def |> Option.map render |> Option.defaultValue "null"
                                { Key = key; Value = v; Origin = originName FromDefault })
                    let localSources  = pl |> Option.map LocalConfig.sources |> Option.defaultValue [] |> List.map (fun s -> s.Name) |> Set.ofList
                    let localInboxes  = pl |> Option.map LocalConfig.inboxes |> Option.defaultValue Map.empty
                    let origin inLocal = if inLocal then "local" else "global"
                    let sources =
                        eff.Sources |> List.map (fun s -> { Kind = "source"; Name = s.Name; Origin = origin (localSources.Contains s.Name) })
                    let inboxes =
                        eff.Inboxes |> Map.toList |> List.map (fun (n, _) -> { Kind = "inbox"; Name = n; Origin = origin (localInboxes.ContainsKey n) })
                    { Settings = settings; Entities = sources @ inboxes }))

    // ── set ─────────────────────────────────────────────────────────────────────

    // Sections `set` may touch. Sources, collections and inbox items have dedicated
    // commands that keep their cross-reference rules.
    let private settableSections = set [ "site"; "okf"; "mcp"; "sync"; "files"; "inbox" ]

    let private parseValue (value: string) : JsonNode =
        try JsonNode.Parse value |> Option.ofObj |> Option.defaultWith (fun () -> JsonValue.Create<string>(null) :> JsonNode)
        with _ -> JsonValue.Create value :> JsonNode

    let private indented = JsonSerializerOptions(WriteIndented = true)

    let set (io: ConfigIO) (scope: ConfigScope) (key: string) (value: string option) : Result<string, string> =
        let path = splitKey key
        match path with
        | [] -> Error "key must not be empty"
        | section :: rest when not (settableSections.Contains section) || rest.IsEmpty ->
            let allowed = settableSections |> Set.toList |> String.concat ", "
            Error $"'{key}' cannot be set: use a dotted key inside one of {allowed} (sources, collections and inbox items have their own commands)"
        | "inbox" :: "items" :: _ ->
            Error "inbox items cannot be set here: use 'eru inbox'"
        | _ ->
        let file = io.Path scope
        io.ReadRaw file
        |> Result.bind (fun raw ->
            match raw with
            | None -> Ok (Config.supportedVersion, $"{{\"version\": {Config.supportedVersion}}}")
            | Some r -> onDiskVersion r |> Result.map (fun v -> v, r))
        |> Result.bind (fun (version, raw) ->
            if version < Config.supportedVersion then
                let flag = if scope = GlobalScope then " --global" else ""
                Error $"{scopeName scope} config is version {version}: run 'eru config migrate{flag}' first"
            else parseNode raw |> Result.map (fun n -> n |> Option.defaultWith (fun () -> JsonObject() :> JsonNode)))
        |> Result.bind (fun root ->
            match root with
            | :? JsonObject as obj ->
                let parents, leaf = List.take (path.Length - 1) path, List.last path
                // Walk, creating (or replacing null with) objects along the way.
                let parent =
                    parents |> List.fold (fun (cur: JsonObject) seg ->
                        match (if cur.ContainsKey seg then cur[seg] else null) with
                        | :? JsonObject as o -> o
                        | _ ->
                            let o = JsonObject()
                            cur[seg] <- o
                            o) obj
                match value with
                | Some v -> parent[leaf] <- parseValue v
                | None   -> parent.Remove leaf |> ignore
                Ok (obj.ToJsonString indented)
            | _ -> Error $"{scopeName scope} config is not a JSON object")
        |> Result.bind (fun text ->
            // Validate before writing: it must deserialize and still merge with the other file.
            let other = match scope with LocalScope -> GlobalScope | GlobalScope -> LocalScope
            let parseOther (parse: string -> Result<'c, string>) =
                io.ReadRaw (io.Path other) |> Result.bind (function Some r -> parse r |> Result.map Some | None -> Ok None)
            let merged =
                match scope with
                | GlobalScope ->
                    io.ParseGlobal text
                    |> Result.bind (fun g -> parseOther io.ParseLocal |> Result.bind (fun l -> Config.merge (Some g) l))
                | LocalScope ->
                    io.ParseLocal text
                    |> Result.bind (fun l -> parseOther io.ParseGlobal |> Result.bind (fun g -> Config.merge g (Some l)))
            merged
            |> Result.mapError (fun e -> $"invalid value for '{key}': {e}")
            |> Result.bind (fun _ -> io.WriteRaw file (text + "\n"))
            |> Result.map (fun () -> file))

    // ── migrate ─────────────────────────────────────────────────────────────────

    let migrate (io: ConfigIO) (scope: ConfigScope) (dryRun: bool) : Result<MigrateOutcome, string> =
        let file = io.Path scope
        io.ReadRaw file
        |> Result.bind (function
            | None -> Error $"no {scopeName scope} config at {file}"
            | Some raw -> onDiskVersion raw |> Result.map (fun v -> v, raw))
        |> Result.bind (fun (version, raw) ->
            if version > Config.supportedVersion then
                Error $"Unsupported {scopeName scope} config version {version} — please upgrade eru"
            elif version = Config.supportedVersion then Ok (AlreadyCurrent version)
            else
                let migrated =
                    match scope with
                    | GlobalScope -> io.ParseGlobal raw |> Result.map io.SerializeGlobal
                    | LocalScope  -> io.ParseLocal raw  |> Result.map io.SerializeLocal
                migrated
                |> Result.bind (fun content ->
                    if dryRun then Ok (Migrated (version, file, None, content))
                    else
                        let backup = file + ".bak"
                        io.WriteRaw backup raw
                        |> Result.bind (fun () -> io.WriteRaw file (content + "\n"))
                        |> Result.map (fun () -> Migrated (version, file, Some backup, content))))
