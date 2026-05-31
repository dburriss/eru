namespace Eru

module ManifestInit =

    type Command = { Force: bool }

    let private emptyManifest = { Version = 1; Files = [] }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadLocalManifest () with
        | Error e -> Error e
        | Ok (Some _) when not cmd.Force ->
            Error ".eru/manifest.json already exists. Use --force to overwrite."
        | _ ->
            match deps.WriteLocalManifest emptyManifest with
            | Error e -> Error e
            | Ok () ->
                if cmd.Force then Ok "Created .eru/manifest.json (overwritten)."
                else Ok "Created .eru/manifest.json."
