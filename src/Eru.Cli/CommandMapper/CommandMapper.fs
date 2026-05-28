module Eru.Cli.CommandMapper

open Argu
open Eru

let (|InitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Init args ->
            Some {
                Init.Command.Force    = args.Contains InitArgs.Force
                Init.Command.IsGlobal = args.Contains InitArgs.Global
                Init.Command.Path     = args.TryGetResult InitArgs.Path
            }
        | _ -> None)

let (|AddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Add args ->
            let cmd : Add.Command = {
                RemotePath     = args.TryGetResult AddArgs.Remote_Path
                Tags           = args.GetResults  AddArgs.Tag
                SourceName     = args.TryGetResult AddArgs.Source
                CollectionName = args.TryGetResult AddArgs.Collection
                Target         = args.TryGetResult AddArgs.Target
                DryRun         = args.Contains    AddArgs.Dryrun
                IsGlobal       = args.Contains    AddArgs.Global
            }
            Some cmd
        | _ -> None)

let (|SearchCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Search args ->
            let query : Search.Query = {
                Terms = args.GetResult(SearchArgs.Terms, defaultValue = [])
                Tags  = args.GetResults SearchArgs.Tag
            }
            Some query
        | _ -> None)

let (|SyncCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Sync args -> Some { Sync.Options.DryRun = args.Contains SyncArgs.Dryrun }
        | _                 -> None)

let (|McpCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Mcp _ -> Some ()
        | _             -> None)

let (|SourceAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Add addArgs ->
                    Some ({
                        Url      = addArgs.GetResult    SourceAddArgs.Url
                        Name     = addArgs.TryGetResult SourceAddArgs.Name
                        Branch   = addArgs.TryGetResult SourceAddArgs.Branch
                        BasePath = addArgs.TryGetResult SourceAddArgs.Basepath
                        IsGlobal = addArgs.Contains     SourceAddArgs.Global
                        DryRun   = addArgs.Contains     SourceAddArgs.Dryrun
                    } : Source.AddCommand)
                | _ -> None)
        | _ -> None)

let (|SourceListCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.List _ -> Some ()
                | _ -> None)
        | _ -> None)

let (|SourceViewCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.View viewArgs ->
                    Some (viewArgs.GetResult SourceViewArgs.Name,
                          viewArgs.Contains SourceViewArgs.Full)
                | _ -> None)
        | _ -> None)

let private parseSourcePath (raw: string) : Result<string * string, string> =
    let idx = raw.IndexOf(':')
    if idx <= 0 then Error $"Invalid source:path format '{raw}' — expected <source>:<remotePath>"
    else Ok (raw.[..idx-1], raw.[idx+1..])

let (|SourceRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Source args ->
            args.TryGetSubCommand() |> Option.bind (function
                | SourceArgs.Remove removeArgs ->
                    Some ({
                        Source.RemoveCommand.Name     = removeArgs.GetResult SourceRemoveArgs.Name
                        Source.RemoveCommand.IsGlobal = removeArgs.Contains  SourceRemoveArgs.Global
                        Source.RemoveCommand.DryRun   = removeArgs.Contains  SourceRemoveArgs.Dryrun
                    })
                | _ -> None)
        | _ -> None)

let (|CollectionCreateCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Collection args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CollectionArgs.Create createArgs ->
                    Some ({
                        Collection.CreateCommand.Name        = createArgs.GetResult    CollectionCreateArgs.Name
                        Collection.CreateCommand.Tags        = createArgs.GetResults   CollectionCreateArgs.Tag
                        Collection.CreateCommand.Description = createArgs.TryGetResult CollectionCreateArgs.Description
                        Collection.CreateCommand.IsGlobal    = createArgs.Contains     CollectionCreateArgs.Global
                        Collection.CreateCommand.DryRun      = createArgs.Contains     CollectionCreateArgs.Dryrun
                    })
                | _ -> None)
        | _ -> None)

let (|ManifestInitCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Init initArgs ->
                    Some ({ Manifest.InitCommand.Force = initArgs.Contains ManifestInitArgs.Force })
                | _ -> None)
        | _ -> None)

let (|ManifestAddCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Add addArgs ->
                    Some ({
                        Manifest.AddFileCommand.Path        = addArgs.GetResult    ManifestAddArgs.Path
                        Manifest.AddFileCommand.Tags        = addArgs.GetResults   ManifestAddArgs.Tag
                        Manifest.AddFileCommand.Description = addArgs.TryGetResult ManifestAddArgs.Description
                        Manifest.AddFileCommand.DryRun      = addArgs.Contains     ManifestAddArgs.Dryrun
                    })
                | _ -> None)
        | _ -> None)

let (|ManifestRemoveCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Remove removeArgs ->
                    Some ({
                        Manifest.RemoveFileCommand.Path   = removeArgs.GetResult ManifestRemoveArgs.Path
                        Manifest.RemoveFileCommand.DryRun = removeArgs.Contains  ManifestRemoveArgs.Dryrun
                    })
                | _ -> None)
        | _ -> None)

let (|ManifestVerifyCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Manifest args ->
            args.TryGetSubCommand() |> Option.bind (function
                | ManifestArgs.Verify _ -> Some ()
                | _ -> None)
        | _ -> None)

let (|CollectionAddFileCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Collection args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CollectionArgs.Add addArgs ->
                    let raw = addArgs.GetResult CollectionAddArgs.File
                    match parseSourcePath raw with
                    | Error e -> eprintfn $"Error: {e}"; None
                    | Ok (source, remotePath) ->
                        Some ({
                            Collection.AddFileCommand.CollectionName = addArgs.GetResult    CollectionAddArgs.Collection
                            Collection.AddFileCommand.Source         = source
                            Collection.AddFileCommand.RemotePath     = remotePath
                            Collection.AddFileCommand.Tags           = addArgs.GetResults   CollectionAddArgs.Tag
                            Collection.AddFileCommand.Description    = addArgs.TryGetResult CollectionAddArgs.Description
                            Collection.AddFileCommand.IsGlobal       = addArgs.Contains     CollectionAddArgs.Global
                            Collection.AddFileCommand.DryRun         = addArgs.Contains     CollectionAddArgs.Dryrun
                        })
                | _ -> None)
        | _ -> None)

let (|CollectionRemoveFileCmd|_|) (r: ParseResults<EruArgs>) =
    r.TryGetSubCommand() |> Option.bind (function
        | EruArgs.Collection args ->
            args.TryGetSubCommand() |> Option.bind (function
                | CollectionArgs.Remove removeArgs ->
                    let raw = removeArgs.GetResult CollectionRemoveFileArgs.File
                    match parseSourcePath raw with
                    | Error e -> eprintfn $"Error: {e}"; None
                    | Ok (source, remotePath) ->
                        Some ({
                            Collection.RemoveFileCommand.CollectionName = removeArgs.GetResult CollectionRemoveFileArgs.Collection
                            Collection.RemoveFileCommand.Source         = source
                            Collection.RemoveFileCommand.RemotePath     = remotePath
                            Collection.RemoveFileCommand.IsGlobal       = removeArgs.Contains  CollectionRemoveFileArgs.Global
                            Collection.RemoveFileCommand.DryRun         = removeArgs.Contains  CollectionRemoveFileArgs.Dryrun
                        })
                | _ -> None)
        | _ -> None)
