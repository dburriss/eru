namespace Eru

open System
open System.Globalization
open System.Text.Json.Nodes

// Config settings as environment variables. The names mirror the config file shape using
// .NET's `__` nesting separator, case-insensitively: `search.mode` is `ERU__SEARCH__MODE`,
// `mcp.refreshIntervalMinutes` is `ERU__MCP__REFRESHINTERVALMINUTES`. List items are indexed
// (`ERU__FILES__BLOCKPATTERNS__0`, `__1`), and so are the objects inside lists
// (`ERU__OKF__FIXTYPES__0__PATTERN`). The variables are folded into a JSON object shaped like
// a config file, so the caller can deserialize it with the same code that reads a file.
//
// A source's own overrides are `ERU__SOURCE__<NAME>__<SECTION>__<KEY>` (e.g.
// `ERU__SOURCE__DOCS__FILES__ALLOWBINARIES`). They can only adjust a source that is already in a
// config file: sources themselves, collections and inbox items stay file-only. The name is
// matched loosely against the configured sources (see Config.normalizeSourceName).
module EnvConfig =

    let prefix = "ERU__"

    // Older single-underscore variable, kept as an alias for `ERU__SEARCH__MODE` (which wins).
    let private searchModeAlias = "ERU_SEARCH_MODE"

    type Layer = {
        // A config-file-shaped object; None when no setting is overridden.
        Json     : JsonObject option
        Warnings : string list
    }

    // Section -> lower-cased key -> the key's name in the config file.
    let private vocabulary : Map<string, Map<string, string>> =
        let keys (names: string list) = names |> List.map (fun n -> n.ToLowerInvariant(), n) |> Map.ofList
        Map.ofList [
            "site",   keys [ "ignorePatterns"; "hideEmptyBundles"; "showSyncStatus"; "theme"; "home"; "defaultView"; "cards"; "layout"; "pages"; "document" ]
            "okf",    keys [ "ignorePatterns"; "fixTypes"; "defaultType" ]
            "search", keys [ "mode"; "ignorePatterns" ]
            "mcp",    keys [ "refreshIntervalMinutes" ]
            "sync",   keys [ "branch"; "commitOnPull"; "stateFile" ]
            "files",  keys [ "blockPatterns"; "allowPatterns"; "allowBinaries" ]
            "inbox",  keys [ "default"; "watchIntervalSeconds" ]
        ]

    // The sections and keys a source can override, in the same shape as `vocabulary`.
    let private sourceVocabulary : Map<string, Map<string, string>> =
        let keys (names: string list) = names |> List.map (fun n -> n.ToLowerInvariant(), n) |> Map.ofList
        Map.ofList [
            "files",  keys [ "blockPatterns"; "allowPatterns"; "allowBinaries" ]
            "search", keys [ "ignorePatterns" ]
            "okf",    keys [ "ignorePatterns" ]
            "site",   keys [ "ignorePatterns" ]
        ]

    // The setting a path names: `source.<name>.files.allowbinaries` is the `files.allowbinaries` setting.
    let private settingPath (path: string list) =
        match path with
        | "source" :: _ :: rest -> rest
        | _ -> path

    // Settings whose value is a list, written as indexed variables.
    let private listPaths =
        set [ "site.ignorepatterns"; "okf.ignorepatterns"; "okf.fixtypes"; "search.ignorepatterns"
              "files.blockpatterns"; "files.allowpatterns"; "site.document.rules" ]

    // Settings that are always strings, so a value such as `123` or `true` is not coerced.
    let private stringPaths =
        set [ "sync.branch"; "sync.statefile"; "search.mode"; "okf.defaulttype"; "inbox.default"
              "site.home"; "site.defaultview"; "site.theme"
              "site.ignorepatterns.*"; "okf.ignorepatterns.*"; "search.ignorepatterns.*"
              "files.blockpatterns.*"; "files.allowpatterns.*"
              "okf.fixtypes.*.pattern"; "okf.fixtypes.*.type" ]

    // Settings that are whole numbers or booleans, checked up front so the error names the variable.
    let private intPaths = set [ "mcp.refreshintervalminutes"; "inbox.watchintervalseconds"; "site.cards.columns" ]
    let private boolPaths = set [ "sync.commitonpull"; "files.allowbinaries"; "site.hideemptybundles"; "enabled" ]

    let private isIndex (seg: string) = seg <> "" && seg |> Seq.forall Char.IsAsciiDigit

    // Path with every array index replaced by `*`, joined with dots.
    let private shape (path: string list) =
        path |> List.map (fun s -> if isIndex s then "*" else s) |> String.concat "."

    let private varName (path: string list) =
        prefix + (path |> List.map (fun s -> s.ToUpperInvariant()) |> String.concat "__")

    let private checkValue (path: string list) (value: string) : Result<unit, string> =
        let key = String.concat "." (settingPath path)
        let text = value.Trim()
        if text = "null" then Ok ()
        elif intPaths.Contains key && not (fst (Int32.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture))) then
            Error $"{varName path} must be a whole number (got '{value}')"
        elif boolPaths.Contains key
             && not (String.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                     || String.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) then
            Error $"{varName path} must be true or false (got '{value}')"
        else Ok ()

    type private Node =
        | Leaf of string
        | Branch of Map<string, Node>

    let rec private insert (path: string list) (value: string) (node: Node option) : Result<Node, string list> =
        match path, node with
        | [], None -> Ok (Leaf value)
        | [], Some _ -> Error path
        | seg :: rest, None ->
            insert rest value None |> Result.map (fun c -> Branch (Map.ofList [ seg, c ]))
        | seg :: rest, Some (Branch m) ->
            insert rest value (Map.tryFind seg m) |> Result.map (fun c -> Branch (Map.add seg c m))
        | _ :: _, Some (Leaf _) -> Error path

    // A value from the environment is text; give it the JSON type the setting expects.
    let private coerce (path: string list) (value: string) : JsonNode =
        let text = value.Trim()
        if text = "null" then null
        elif stringPaths.Contains (shape (settingPath path)) then JsonValue.Create<string>(value) :> JsonNode
        elif String.Equals(text, "true", StringComparison.OrdinalIgnoreCase) then JsonValue.Create<bool>(true) :> JsonNode
        elif String.Equals(text, "false", StringComparison.OrdinalIgnoreCase) then JsonValue.Create<bool>(false) :> JsonNode
        else
            match Int64.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
            | true, n -> JsonValue.Create<int64>(n) :> JsonNode
            | _ -> JsonValue.Create<string>(value) :> JsonNode

    // Only the first two levels hold camelCase names; deeper keys are lower-case, or user-chosen
    // ids (page ids) that are kept as written.
    let private canonical (depth: int) (parent: string list) (seg: string) : string =
        match depth, parent with
        | 1, [ section ] -> vocabulary |> Map.tryFind section |> Option.bind (Map.tryFind seg) |> Option.defaultValue seg
        | 3, [ "source"; _; section ] -> sourceVocabulary |> Map.tryFind section |> Option.bind (Map.tryFind seg) |> Option.defaultValue seg
        | _ -> seg

    let rec private toJson (path: string list) (node: Node) : Result<JsonNode, string> =
        match node with
        | Leaf v -> Ok (coerce path v)
        | Branch m ->
            let indexed = m |> Map.toList |> List.forall (fun (k, _) -> isIndex k)
            let anyIndexed = m |> Map.toList |> List.exists (fun (k, _) -> isIndex k)
            if anyIndexed && not indexed then
                Error $"{varName path}: mixes indexed and named entries"
            elif indexed then
                let items = m |> Map.toList |> List.map (fun (k, v) -> int k, v) |> List.sortBy fst
                if items |> List.mapi (fun i (n, _) -> i = n) |> List.forall id |> not then
                    Error $"{varName path}: indexes must run 0, 1, 2, … without gaps"
                else
                    items
                    |> List.fold (fun acc (n, child) ->
                        acc |> Result.bind (fun (arr: JsonArray) ->
                            toJson (path @ [ string n ]) child
                            |> Result.map (fun j -> arr.Add(j); arr))) (Ok (JsonArray()))
                    |> Result.map (fun a -> a :> JsonNode)
            else
                m
                |> Map.toList
                |> List.fold (fun acc (k, child) ->
                    acc |> Result.bind (fun (o: JsonObject) ->
                        toJson (path @ [ k ]) child
                        |> Result.map (fun j -> o[canonical path.Length path k] <- j; o))) (Ok (JsonObject()))
                |> Result.map (fun o -> o :> JsonNode)

    /// Reads the settings overrides from an environment (name/value pairs). Names that are not
    /// settings come back as warnings and are skipped; a variable that cannot be folded into the
    /// config shape (a list given as a single value, a gap in the indexes) is an error.
    let read (env: (string * string) seq) : Result<Layer, string> =
        let warnings = ResizeArray<string>()

        // (path, value) entries; the alias goes first so ERU__SEARCH__MODE overwrites it.
        let entries =
            env
            |> Seq.filter (fun (_, v) -> not (String.IsNullOrWhiteSpace v))
            |> Seq.sortBy fst
            |> Seq.choose (fun (name, value) ->
                if name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) then
                    let segs = name.Substring(prefix.Length).ToLowerInvariant().Split("__") |> List.ofArray
                    if segs |> List.exists (fun s -> s = "") then
                        warnings.Add $"{name}: ignored (empty name segment)"
                        None
                    else Some (1, segs, value)
                elif String.Equals(name, searchModeAlias, StringComparison.OrdinalIgnoreCase) then
                    Some (0, [ "search"; "mode" ], value)
                else None)
            |> Seq.sortBy (fun (rank, _, _) -> rank)
            |> Seq.map (fun (_, p, v) -> p, v)
            |> Map.ofSeq   // later wins, so the ERU__ form beats the alias

        let supported =
            entries
            |> Map.toList
            |> List.filter (fun (path, _) ->
                match path with
                | section :: key :: _ when vocabulary.ContainsKey section && vocabulary[section].ContainsKey key -> true
                | [ "source"; _; "enabled" ] -> true
                | "source" :: _ :: section :: key :: _ when sourceVocabulary.ContainsKey section && sourceVocabulary[section].ContainsKey key -> true
                | "source" :: _ :: _ :: _ :: _ ->
                    warnings.Add $"{varName path}: ignored (a source can override only enabled, files.*, search.ignorePatterns, okf.ignorePatterns and site.ignorePatterns)"
                    false
                | section :: _ when section = "source" || section = "collection" ->
                    warnings.Add $"{varName path}: ignored (sources and collections are configured in the config file only; a source's own settings are ERU__SOURCE__<NAME>__<SECTION>__<KEY>)"
                    false
                | "inbox" :: "items" :: _ ->
                    warnings.Add $"{varName path}: ignored (inbox items are configured in the config file only)"
                    false
                | _ ->
                    warnings.Add $"{varName path}: ignored (not a known setting)"
                    false)

        let build =
            supported
            |> List.fold (fun acc (path, value) ->
                acc |> Result.bind (fun root ->
                    if listPaths.Contains (String.concat "." path) then
                        Error $"{varName path} is a list: set it as {varName path}__0, {varName path}__1, …"
                    else
                        checkValue path value
                        |> Result.bind (fun () ->
                            insert path value (Some root)
                            |> Result.mapError (fun _ -> $"{varName path} conflicts with another ERU__ variable"))))
                (Ok (Branch Map.empty))

        build
        |> Result.bind (fun root ->
            match root with
            | Branch m when m.IsEmpty -> Ok { Json = None; Warnings = List.ofSeq warnings }
            | _ ->
                toJson [] root
                |> Result.map (fun j ->
                    let o = j :?> JsonObject
                    // Section names are lower-case in the file, like their env names.
                    o["version"] <- JsonValue.Create<int>(Config.supportedVersion)
                    // `source` came out keyed by name; the file shape is `source.items[]` with a `name`.
                    match o["source"] with
                    | :? JsonObject as bySource ->
                        let items = JsonArray()
                        for KeyValue(name, body) in bySource |> Seq.toList do
                            let item = body.DeepClone() :?> JsonObject
                            item["name"] <- JsonValue.Create<string>(name)
                            items.Add(item)
                        let wrapper = JsonObject()
                        wrapper["items"] <- items
                        o["source"] <- wrapper
                    | _ -> ()
                    { Json = Some o; Warnings = List.ofSeq warnings }))
