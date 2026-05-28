namespace Eru

module Manifest =

    type InitCommand = {
        Force : bool
    }

    type AddFileCommand = {
        Path        : string
        Tags        : string list
        Description : string option
        DryRun      : bool
    }

    type RemoveFileCommand = {
        Path   : string
        DryRun : bool
    }

    let private emptyManifest = { Version = 1; Files = [] }

    let init (deps: Deps) (cmd: InitCommand) : int =
        match deps.ReadLocalManifest () with
        | Error e -> eprintfn $"Error: {e}"; 1
        | Ok (Some _) when not cmd.Force ->
            eprintfn "Error: .eru/manifest.json already exists. Use --force to overwrite."; 1
        | _ ->
            if cmd.Force then
                match deps.WriteLocalManifest emptyManifest with
                | Ok ()   -> printfn "Created .eru/manifest.json (overwritten)."; 0
                | Error e -> eprintfn $"Error: {e}"; 1
            else
                match deps.WriteLocalManifest emptyManifest with
                | Ok ()   -> printfn "Created .eru/manifest.json."; 0
                | Error e -> eprintfn $"Error: {e}"; 1

    let addFile (deps: Deps) (cmd: AddFileCommand) : int =
        match deps.ReadLocalManifest () with
        | Error e -> eprintfn $"Error: {e}"; 1
        | Ok None ->
            eprintfn "Error: no .eru/manifest.json found. Run 'eru manifest init' first."; 1
        | Ok (Some manifest) ->
            if manifest.Files |> List.exists (fun f -> f.Path = cmd.Path) then
                eprintfn $"Error: '{cmd.Path}' is already in the manifest."; 1
            else
                let entry : ManifestFileRef = {
                    Path        = cmd.Path
                    Tags        = cmd.Tags
                    Description = cmd.Description
                }
                if cmd.DryRun then
                    printfn $"Would add '{cmd.Path}' to .eru/manifest.json."; 0
                else
                    let updated = { manifest with Files = manifest.Files @ [entry] }
                    match deps.WriteLocalManifest updated with
                    | Ok ()   -> printfn $"Added '{cmd.Path}' to .eru/manifest.json."; 0
                    | Error e -> eprintfn $"Error: {e}"; 1

    let removeFile (deps: Deps) (cmd: RemoveFileCommand) : int =
        match deps.ReadLocalManifest () with
        | Error e -> eprintfn $"Error: {e}"; 1
        | Ok None ->
            eprintfn "Error: no .eru/manifest.json found. Run 'eru manifest init' first."; 1
        | Ok (Some manifest) ->
            if not (manifest.Files |> List.exists (fun f -> f.Path = cmd.Path)) then
                eprintfn $"Error: '{cmd.Path}' not found in .eru/manifest.json."; 1
            elif cmd.DryRun then
                printfn $"Would remove '{cmd.Path}' from .eru/manifest.json."; 0
            else
                let updated = { manifest with Files = manifest.Files |> List.filter (fun f -> f.Path <> cmd.Path) }
                match deps.WriteLocalManifest updated with
                | Ok ()   -> printfn $"Removed '{cmd.Path}' from .eru/manifest.json."; 0
                | Error e -> eprintfn $"Error: {e}"; 1

    let verify (deps: Deps) : int =
        match deps.ReadLocalManifest () with
        | Error e -> eprintfn $"Error: {e}"; 1
        | Ok None ->
            eprintfn "Error: no .eru/manifest.json found. Run 'eru manifest init' first."; 1
        | Ok (Some manifest) ->
            if manifest.Files.IsEmpty then
                printfn "Manifest is empty — nothing to verify."; 0
            else
                let missing =
                    manifest.Files
                    |> List.filter (fun f -> deps.ResolveLocalGlob f.Path |> List.isEmpty)
                if missing.IsEmpty then
                    printfn $"All {manifest.Files.Length} manifest reference(s) verified."; 0
                else
                    missing |> List.iter (fun f -> eprintfn $"  missing: {f.Path}")
                    eprintfn $"{missing.Length} reference(s) resolved to no local files."
                    1
