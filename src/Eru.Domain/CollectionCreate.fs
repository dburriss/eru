namespace Eru

module CollectionCreate =

    type Command = {
        Name        : string
        Tags        : string list
        Description : string option
        IsGlobal    : bool
        DryRun      : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
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
            | Error e -> Error e
            | Ok g ->
                if g.Collections |> List.exists (fun c -> c.Name = cmd.Name) then
                    Error $"collection '{cmd.Name}' already exists in global config."
                elif cmd.DryRun then
                    Ok $"Would create collection '{cmd.Name}' in global config."
                else
                    let updated = { g with Collections = g.Collections @ [newCol] }
                    match deps.WriteGlobalConfig updated with
                    | Ok ()   -> Ok $"Created collection '{cmd.Name}' in global config."
                    | Error e -> Error e
        else
            match deps.ReadLocalConfig () with
            | Error e -> Error e
            | Ok None -> Error "no .eru/config.json found. Run 'eru init' first."
            | Ok (Some local) ->
                if local.Collections |> List.exists (fun c -> c.Name = cmd.Name) then
                    Error $"collection '{cmd.Name}' already exists in .eru/config.json."
                elif cmd.DryRun then
                    Ok $"Would create collection '{cmd.Name}' in .eru/config.json."
                else
                    let updated = { local with Collections = local.Collections @ [newCol] }
                    match deps.WriteLocalConfig updated with
                    | Ok ()   -> Ok $"Created collection '{cmd.Name}' in .eru/config.json."
                    | Error e -> Error e
