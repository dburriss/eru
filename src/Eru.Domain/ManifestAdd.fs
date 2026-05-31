namespace Eru

module ManifestAdd =

    type Command = {
        Path        : string
        Tags        : string list
        Description : string option
        DryRun      : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadLocalManifest () with
        | Error e -> Error e
        | Ok None -> Error "no .eru/manifest.json found. Run 'eru manifest init' first."
        | Ok (Some manifest) ->
            if manifest.Files |> List.exists (fun f -> f.Path = cmd.Path) then
                Error $"'{cmd.Path}' is already in the manifest."
            else
                let entry : ManifestFileRef = {
                    Path        = cmd.Path
                    Tags        = cmd.Tags
                    Description = cmd.Description
                }
                if cmd.DryRun then
                    Ok $"Would add '{cmd.Path}' to .eru/manifest.json."
                else
                    let updated = { manifest with Files = manifest.Files @ [entry] }
                    match deps.WriteLocalManifest updated with
                    | Ok ()   -> Ok $"Added '{cmd.Path}' to .eru/manifest.json."
                    | Error e -> Error e
