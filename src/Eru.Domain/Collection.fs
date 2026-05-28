namespace Eru

module Collection =
    type CreateCommand = {
        Name        : string
        Tags        : string list
        Description : string option
        IsGlobal    : bool
        DryRun      : bool
    }

    type AddFileCommand = {
        CollectionName : string
        Source         : string
        RemotePath     : string
        Tags           : string list
        Description    : string option
        IsGlobal       : bool
        DryRun         : bool
    }

    type RemoveFileCommand = {
        CollectionName : string
        Source         : string
        RemotePath     : string
        IsGlobal       : bool
        DryRun         : bool
    }

    let create (deps: Deps) (cmd: CreateCommand) : int =
        let newCol : CollectionConfig = {
            Name        = cmd.Name
            Tags        = cmd.Tags
            Files       = []
            Description = cmd.Description
        }
        if cmd.IsGlobal then
            let cfg =
                match deps.ReadGlobalConfig () with
                | Ok (Some g) -> Ok g
                | Ok None     -> Ok { Version = 1; DefaultSources = []; Collections = []; Defaults = None }
                | Error e     -> Error e
            match cfg with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok g ->
                if g.Collections |> List.exists (fun c -> c.Name = cmd.Name) then
                    eprintfn $"Error: collection '{cmd.Name}' already exists in global config."; 1
                elif cmd.DryRun then
                    printfn $"Would create collection '{cmd.Name}' in global config."; 0
                else
                    let updated = { g with Collections = g.Collections @ [newCol] }
                    match deps.WriteGlobalConfig updated with
                    | Ok ()   -> printfn $"Created collection '{cmd.Name}' in global config."; 0
                    | Error e -> eprintfn $"Error: {e}"; 1
        else
            match deps.ReadLocalConfig () with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok None ->
                eprintfn "Error: no .eru/config.json found. Run 'eru init' first."; 1
            | Ok (Some local) ->
                if local.Collections |> List.exists (fun c -> c.Name = cmd.Name) then
                    eprintfn $"Error: collection '{cmd.Name}' already exists in .eru/config.json."; 1
                elif cmd.DryRun then
                    printfn $"Would create collection '{cmd.Name}' in .eru/config.json."; 0
                else
                    let updated = { local with Collections = local.Collections @ [newCol] }
                    match deps.WriteLocalConfig updated with
                    | Ok ()   -> printfn $"Created collection '{cmd.Name}' in .eru/config.json."; 0
                    | Error e -> eprintfn $"Error: {e}"; 1

    let addFile (deps: Deps) (cmd: AddFileCommand) : int =
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
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok g ->
                match g.Collections |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None ->
                    eprintfn $"Error: collection '{cmd.CollectionName}' not found in global config."; 1
                | Some col ->
                    if col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath) then
                        eprintfn $"Error: '{cmd.Source}:{cmd.RemotePath}' is already in collection '{cmd.CollectionName}'."; 1
                    elif cmd.DryRun then
                        printfn $"Would add '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in global config."; 0
                    else
                        let updatedCol  = { col with Files = col.Files @ [fileRef] }
                        let updatedCols = g.Collections |> List.map (fun c -> if c.Name = cmd.CollectionName then updatedCol else c)
                        let updated     = { g with Collections = updatedCols }
                        match deps.WriteGlobalConfig updated with
                        | Ok ()   -> printfn $"Added '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in global config."; 0
                        | Error e -> eprintfn $"Error: {e}"; 1
        else
            match deps.ReadLocalConfig () with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok None ->
                eprintfn "Error: no .eru/config.json found. Run 'eru init' first."; 1
            | Ok (Some local) ->
                match local.Collections |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None ->
                    eprintfn $"Error: collection '{cmd.CollectionName}' not found in .eru/config.json."; 1
                | Some col ->
                    if col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath) then
                        eprintfn $"Error: '{cmd.Source}:{cmd.RemotePath}' is already in collection '{cmd.CollectionName}'."; 1
                    elif cmd.DryRun then
                        printfn $"Would add '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in .eru/config.json."; 0
                    else
                        let updatedCol  = { col with Files = col.Files @ [fileRef] }
                        let updatedCols = local.Collections |> List.map (fun c -> if c.Name = cmd.CollectionName then updatedCol else c)
                        let updated     = { local with Collections = updatedCols }
                        match deps.WriteLocalConfig updated with
                        | Ok ()   -> printfn $"Added '{cmd.Source}:{cmd.RemotePath}' to collection '{cmd.CollectionName}' in .eru/config.json."; 0
                        | Error e -> eprintfn $"Error: {e}"; 1

    let removeFile (deps: Deps) (cmd: RemoveFileCommand) : int =
        if cmd.IsGlobal then
            match deps.ReadGlobalConfig () with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok None -> eprintfn "Error: no global config found."; 1
            | Ok (Some g) ->
                match g.Collections |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None ->
                    eprintfn $"Error: collection '{cmd.CollectionName}' not found in global config."; 1
                | Some col ->
                    if not (col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath)) then
                        eprintfn $"Error: '{cmd.Source}:{cmd.RemotePath}' not found in collection '{cmd.CollectionName}'."; 1
                    elif cmd.DryRun then
                        printfn $"Would remove '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in global config."; 0
                    else
                        let remaining   = col.Files |> List.filter (fun f -> not (f.Source = cmd.Source && f.RemotePath = cmd.RemotePath))
                        let updatedCols =
                            if remaining.IsEmpty then
                                g.Collections |> List.filter (fun c -> c.Name <> cmd.CollectionName)
                            else
                                g.Collections |> List.map (fun c -> if c.Name = cmd.CollectionName then { col with Files = remaining } else c)
                        let updated = { g with Collections = updatedCols }
                        match deps.WriteGlobalConfig updated with
                        | Ok () ->
                            if remaining.IsEmpty then
                                printfn $"Removed last file from collection '{cmd.CollectionName}'; collection entry removed from global config."
                            else
                                printfn $"Removed '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in global config."
                            0
                        | Error e -> eprintfn $"Error: {e}"; 1
        else
            match deps.ReadLocalConfig () with
            | Error e -> eprintfn $"Error: {e}"; 1
            | Ok None -> eprintfn "Error: no .eru/config.json found. Run 'eru init' first."; 1
            | Ok (Some local) ->
                match local.Collections |> List.tryFind (fun c -> c.Name = cmd.CollectionName) with
                | None ->
                    eprintfn $"Error: collection '{cmd.CollectionName}' not found in .eru/config.json."; 1
                | Some col ->
                    if not (col.Files |> List.exists (fun f -> f.Source = cmd.Source && f.RemotePath = cmd.RemotePath)) then
                        eprintfn $"Error: '{cmd.Source}:{cmd.RemotePath}' not found in collection '{cmd.CollectionName}'."; 1
                    elif cmd.DryRun then
                        printfn $"Would remove '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in .eru/config.json."; 0
                    else
                        let remaining   = col.Files |> List.filter (fun f -> not (f.Source = cmd.Source && f.RemotePath = cmd.RemotePath))
                        let updatedCols =
                            if remaining.IsEmpty then
                                local.Collections |> List.filter (fun c -> c.Name <> cmd.CollectionName)
                            else
                                local.Collections |> List.map (fun c -> if c.Name = cmd.CollectionName then { col with Files = remaining } else c)
                        let updated = { local with Collections = updatedCols }
                        match deps.WriteLocalConfig updated with
                        | Ok () ->
                            if remaining.IsEmpty then
                                printfn $"Removed last file from collection '{cmd.CollectionName}'; collection entry removed from .eru/config.json."
                            else
                                printfn $"Removed '{cmd.Source}:{cmd.RemotePath}' from collection '{cmd.CollectionName}' in .eru/config.json."
                            0
                        | Error e -> eprintfn $"Error: {e}"; 1
