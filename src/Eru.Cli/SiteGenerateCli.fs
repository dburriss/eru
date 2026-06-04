module Eru.Cli.SiteGenerateCli

open Argu
open Eru
open Eru.Site

let (|SiteGenerateCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Site args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SiteArgs.Generate generateArgs -> Some generateArgs)
        | _ -> None)

let run (deps: Deps) (args: ParseResults<SiteGenerateArgs>) : int =
    let outputDir   = args.TryGetResult(SiteGenerateArgs.Output)     |> Option.defaultValue "./cache-site/"
    let openBrowser = args.Contains SiteGenerateArgs.Open
    let customCss   = args.TryGetResult(SiteGenerateArgs.Custom_Css)

    let cfgResult =
        let globalCfg = match deps.ReadGlobalConfig() with Ok o -> o | _ -> None
        let localCfg  = match deps.ReadLocalConfig()  with Ok o -> o | _ -> None
        Config.merge globalCfg localCfg
        |> Result.map (fun eff -> Config.withManifests deps.ReadCachedManifest eff)

    match cfgResult with
    | Error e -> eprintfn "Error reading config: %s" e; 1
    | Ok cfg ->
        let opts = { SiteGenerator.GenerateOptions.defaults with
                        OutputDir   = outputDir
                        OpenBrowser = openBrowser
                        Theme       = { SiteGenerator.GenerateOptions.defaults.Theme with CustomCssPath = customCss } }
        match SiteGenerator.generate deps cfg opts with
        | Ok () ->
            printfn "Site generated at %s" (System.IO.Path.GetFullPath outputDir)
            0
        | Error e ->
            eprintfn "Error: %s" e
            1
