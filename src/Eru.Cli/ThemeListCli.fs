module Eru.Cli.ThemeListCli

open Argu
open Spectre.Console
open System.Text.Json
open Eru
open Eru.Site
open Eru.Cli.OutputFormat

type Cmd = { Format: OutputFormat }

let (|ThemeListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Theme args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ThemeArgs.List listArgs ->
                    Some { Format = parseFormat (listArgs.TryGetResult ThemeListArgs.Output) }
                | _ -> None)
        | _ -> None)

type private Row = { Name: string; Active: bool; Origin: string; Description: string }

let private rows (deps: Deps) (active: string) : Row list =
    Themes.discover deps
    |> List.map (fun (name, result) ->
        match result with
        | Ok t ->
            { Name = name; Active = (name = active); Origin = Themes.originLabel t.Origin
              Description = t.Description |> Option.defaultValue "" }
        | Error e -> { Name = name; Active = (name = active); Origin = "invalid"; Description = e })

let private activeTheme (deps: Deps) : string =
    let globalCfg = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
    let localCfg  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
    match DepsConfig.merge deps globalCfg localCfg with
    | Ok eff -> eff.Site.Theme |> Option.defaultValue SiteVocabulary.defaultTheme
    | Error _ -> SiteVocabulary.defaultTheme

let run (deps: Deps) (cmd: Cmd) : int =
    let rows = rows deps (activeTheme deps)
    match cmd.Format with
    | Text ->
        for r in rows do
            let mark = if r.Active then "*" else " "
            let desc = if r.Description = "" then "" else $"  {r.Description}"
            printfn $"{mark} {r.Name}  [{r.Origin}]{desc}"
    | Json ->
        let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        printfn "%s" (JsonSerializer.Serialize(rows, opts))
    | Table ->
        let t = makeTable [ "Name"; "Active"; "Source"; "Description" ]
        for r in rows do
            t.AddRowEscaped(r.Name, (if r.Active then "*" else ""), r.Origin, r.Description) |> ignore
        AnsiConsole.Write(t)
    0
