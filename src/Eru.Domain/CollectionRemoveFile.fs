namespace Eru

module CollectionRemoveFile =

    type Command = {
        CollectionName : string
        Source         : string
        RemotePath     : string
        IsGlobal       : bool
        DryRun         : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        if cmd.IsGlobal then
            match deps.ReadGlobalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no global config found."
            | Ok (Some g) ->
                match (GlobalConfig.collections g) |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None -> Error $"collection '{cmd.CollectionName}' not found in global config."
                | Some col ->
                    if not (col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath)) then
                        Error $"'{cmd.Source}:{cmd.RemotePath}' not found in collection '{cmd.CollectionName}'."
                    elif cmd.DryRun then
                        Ok $"Would remove '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in global config."
                    else
                        let remaining   = col.Files |> List.filter (fun f -> not (f.Source = cmd.Source && f.RemotePath = cmd.RemotePath))
                        let updatedCols =
                            if remaining.IsEmpty then
                                (GlobalConfig.collections g) |> List.filter (fun c -> c.Name <> cmd.CollectionName)
                            else
                                (GlobalConfig.collections g) |> List.map (fun c -> if c.Name = cmd.CollectionName then { col with Files = remaining } else c)
                        let updated = GlobalConfig.withCollections (updatedCols) g
                        match deps.WriteGlobalConfig updated with
                        | Error e -> Error e
                        | Ok () ->
                            if remaining.IsEmpty then
                                Ok $"Removed last file from collection '{cmd.CollectionName}'; collection entry removed from global config."
                            else
                                Ok $"Removed '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in global config."
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                match (LocalConfig.collections local) |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None -> Error $"collection '{cmd.CollectionName}' not found in .eru/config.json."
                | Some col ->
                    if not (col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath)) then
                        Error $"'{cmd.Source}:{cmd.RemotePath}' not found in collection '{cmd.CollectionName}'."
                    elif cmd.DryRun then
                        Ok $"Would remove '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in .eru/config.json."
                    else
                        let remaining   = col.Files |> List.filter (fun f -> not (f.Source = cmd.Source && f.RemotePath = cmd.RemotePath))
                        let updatedCols =
                            if remaining.IsEmpty then
                                (LocalConfig.collections local) |> List.filter (fun c -> c.Name <> cmd.CollectionName)
                            else
                                (LocalConfig.collections local) |> List.map (fun c -> if c.Name = cmd.CollectionName then { col with Files = remaining } else c)
                        let updated = LocalConfig.withCollections (updatedCols) local
                        match deps.WriteLocalConfig updated with
                        | Error e -> Error e
                        | Ok () ->
                            if remaining.IsEmpty then
                                Ok $"Removed last file from collection '{cmd.CollectionName}'; collection entry removed from .eru/config.json."
                            else
                                Ok $"Removed '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in .eru/config.json."
