namespace Eru

module ManifestRemove =

    type Command = {
        Path   : string
        DryRun : bool
    }

    let execute (deps: Deps) (cmd: Command) : Result<string, string> =
        match deps.ReadLocalManifest () with
        | Error e -> Error e
        | Ok None -> Error "no .eru/manifest.json found. Run 'eru manifest init' first."
        | Ok (Some manifest) ->
            if not (manifest.Files |> List.exists (fun f -> f.Path = cmd.Path)) then
                Error $"'{cmd.Path}' not found in .eru/manifest.json."
            elif cmd.DryRun then
                Ok $"Would remove '{cmd.Path}' from .eru/manifest.json."
            else
                let updated = { manifest with Files = manifest.Files |> List.filter (fun f -> f.Path <> cmd.Path) }
                match deps.WriteLocalManifest updated with
                | Ok ()   -> Ok $"Removed '{cmd.Path}' from .eru/manifest.json."
                | Error e -> Error e
