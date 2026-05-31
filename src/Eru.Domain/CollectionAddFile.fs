namespace Eru

module CollectionAddFile =

    type Command = {
        CollectionName : string
        Source         : string
        RemotePath     : string
        Tags           : string list
        Description    : string option
        IsGlobal       : bool
        DryRun         : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        let fileRef : CollectionFileRef = {
            Source      = cmd.Source
            RemotePath  = cmd.RemotePath
            Tags        = cmd.Tags
            Description = cmd.Description
        }
        if cmd.IsGlobal then
            let cfg =
                match deps.ReadGlobalConfig () with
                | Ok (Some g) -> Ok g
                | Ok None     -> Ok { Version = 1; DefaultSources = []; Collections = []; Defaults = None }
                | Error e     -> Error e
            match cfg with
            | Error e -> Error e
            | Ok g ->
                match g.Collections |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None -> Error $"collection '{cmd.CollectionName}' not found in global config."
                | Some col ->
                    if col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath) then
                        Error $"'{cmd.Source}:{cmd.RemotePath}' is already in collection '{cmd.CollectionName}'."
                    elif cmd.DryRun then
                        Ok $"Would add '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in global config."
                    else
                        let updatedCol  = { col with Files = col.Files @ [fileRef] }
                        let updatedCols = g.Collections |> List.map (fun c -> if c.Name = cmd.CollectionName then updatedCol else c)
                        let updated     = { g with Collections = updatedCols }
                        match deps.WriteGlobalConfig updated with
                        | Ok ()   -> Ok $"Added '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in global config."
                        | Error e -> Error e
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                match local.Collections |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None -> Error $"collection '{cmd.CollectionName}' not found in .eru/config.json."
                | Some col ->
                    if col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath) then
                        Error $"'{cmd.Source}:{cmd.RemotePath}' is already in collection '{cmd.CollectionName}'."
                    elif cmd.DryRun then
                        Ok $"Would add '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in .eru/config.json."
                    else
                        let updatedCol  = { col with Files = col.Files @ [fileRef] }
                        let updatedCols = local.Collections |> List.map (fun c -> if c.Name = cmd.CollectionName then updatedCol else c)
                        let updated     = { local with Collections = updatedCols }
                        match deps.WriteLocalConfig updated with
                        | Ok ()   -> Ok $"Added '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in .eru/config.json."
                        | Error e -> Error e
