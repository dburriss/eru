module Eru.Cli.ConfigCli

open System.Text.Json
open Argu
open Spectre.Console
open Eru
open Eru.Adapters
open Eru.Cli.OutputFormat

type Cmd =
    | List    of OutputFormat
    | View    of OutputFormat
    | Get     of key: string * OutputFormat
    | Set     of scope: ConfigScope * key: string * value: string option * OutputFormat
    | Migrate of scope: ConfigScope * dryRun: bool * OutputFormat

let (|ConfigCommand|_|) (r: ParseResults<EruArgs>) : Cmd option =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Config args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ConfigArgs.List a -> Some (List (parseFormat (a.TryGetResult ConfigListArgs.Output)))
                | ConfigArgs.View a -> Some (View (parseFormat (a.TryGetResult ConfigViewArgs.Output)))
                | ConfigArgs.Get a  -> Some (Get (a.GetResult ConfigGetArgs.Key, parseFormat (a.TryGetResult ConfigGetArgs.Output)))
                | ConfigArgs.Set a ->
                    let scope = if a.Contains ConfigSetArgs.Global then GlobalScope else LocalScope
                    let kv    = a.TryGetResult ConfigSetArgs.Key_Value |> Option.defaultValue []
                    let key   = kv |> List.tryHead |> Option.defaultValue ""
                    // Some "" (no value given without --unset) is rejected in `runSet`.
                    let value = if a.Contains ConfigSetArgs.Unset then None else Some (kv |> List.skip (min 1 kv.Length) |> String.concat " ")
                    Some (Set (scope, key, value, parseFormat (a.TryGetResult ConfigSetArgs.Output)))
                | ConfigArgs.Migrate a ->
                    let scope = if a.Contains ConfigMigrateArgs.Global then GlobalScope else LocalScope
                    Some (Migrate (scope, a.Contains ConfigMigrateArgs.Dryrun, parseFormat (a.TryGetResult ConfigMigrateArgs.Output)))
                | _ -> None)
        | _ -> None)

let private jsonOpts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

let private scopeName = function LocalScope -> "local" | GlobalScope -> "global"

let private versionText (v: int option) = v |> Option.map string |> Option.defaultValue "-"

let private fileStatus (r: ConfigCmd.FileRow) =
    match r.Problem with
    | Some p -> $"error: {p}"
    | None when not r.Exists -> "missing"
    | None when r.NeedsMigration -> "needs migration"
    | None -> "ok"

let private runList (io: ConfigIO) (fmt: OutputFormat) : int =
    let rows = ConfigCmd.list io
    match fmt with
    | Json -> printfn "%s" (JsonSerializer.Serialize(rows, jsonOpts))
    | Text ->
        for r in rows do
            printfn "%s  %s  version: %s  [%s]" r.Scope r.Path (versionText r.Version) (fileStatus r)
    | Table ->
        let t = makeTable [ "Scope"; "Path"; "Version"; "Status" ]
        for r in rows do t.AddRow(r.Scope, Markup.Escape r.Path, versionText r.Version, Markup.Escape (fileStatus r)) |> ignore
        AnsiConsole.Write t
    if fmt <> Json && rows |> List.exists (fun r -> r.NeedsMigration) then
        eprintfn "Hint: run 'eru config migrate' (or 'eru config migrate --global') to upgrade outdated files."
    0

let private runView (io: ConfigIO) (fmt: OutputFormat) : int =
    match ConfigCmd.view io with
    | Error e -> renderError e; 1
    | Ok v ->
        match fmt with
        | Json -> printfn "%s" (JsonSerializer.Serialize(v, jsonOpts))
        | Text ->
            for s in v.Settings do printfn "%s = %s  [%s]" s.Key s.Value s.Origin
            for e in v.Entities do printfn "%s %s  [%s]" e.Kind e.Name e.Origin
        | Table ->
            let t = makeTable [ "Key"; "Value"; "Origin" ]
            for s in v.Settings do t.AddRow(s.Key, Markup.Escape s.Value, s.Origin) |> ignore
            for e in v.Entities do t.AddRow(e.Kind, Markup.Escape e.Name, e.Origin) |> ignore
            AnsiConsole.Write t
        0

let private runGet (io: ConfigIO) (key: string) (fmt: OutputFormat) : int =
    match ConfigCmd.get io key with
    | Error e -> renderError e; 1
    | Ok s ->
        match fmt with
        | Json  -> printfn "%s" (JsonSerializer.Serialize(s, jsonOpts))
        | Text  -> printfn "%s" s.Value
        | Table ->
            let t = makeTable [ "Key"; "Value"; "Origin" ]
            t.AddRow(s.Key, Markup.Escape s.Value, s.Origin) |> ignore
            AnsiConsole.Write t
        0

let private runSet (io: ConfigIO) scope key (value: string option) (fmt: OutputFormat) : int =
    match value with
    | Some "" -> renderError "set needs a value (or use --unset to remove the key)"; 1
    | _ ->
        match ConfigCmd.set io scope key value with
        | Error e -> renderError e; 1
        | Ok path ->
            let verb = if value.IsSome then "Set" else "Unset"
            renderMessage $"{verb} {key} in {scopeName scope} config ({path})" fmt
            0

let private runMigrate (io: ConfigIO) scope dryRun (fmt: OutputFormat) : int =
    match ConfigCmd.migrate io scope dryRun with
    | Error e -> renderError e; 1
    | Ok (ConfigCmd.AlreadyCurrent v) ->
        renderMessage $"{scopeName scope} config is already at version {v}; nothing to do" fmt
        0
    | Ok (ConfigCmd.Migrated (from, path, backup, content)) ->
        if dryRun then
            match fmt with
            | Json -> printfn "%s" (JsonSerializer.Serialize({| scope = scopeName scope; path = path; fromVersion = from; toVersion = Config.supportedVersion; dryRun = true; content = content |}, jsonOpts))
            | _ -> printfn "%s" content
        else
            let bak = backup |> Option.map (fun b -> $" (backup: {b})") |> Option.defaultValue ""
            renderMessage $"Migrated {scopeName scope} config from version {from} to {Config.supportedVersion}: {path}{bak}" fmt
        0

let run (io: ConfigIO) (cmd: Cmd) : int =
    match cmd with
    | List fmt                          -> runList io fmt
    | View fmt                          -> runView io fmt
    | Get (key, fmt)                    -> runGet io key fmt
    | Set (scope, key, value, fmt)      -> runSet io scope key value fmt
    | Migrate (scope, dryRun, fmt)      -> runMigrate io scope dryRun fmt
