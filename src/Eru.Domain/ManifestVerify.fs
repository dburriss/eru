namespace Eru

module ManifestVerify =

    type VerifyResult = {
        Total   : int
        Missing : string list
    }

    let execute (deps: Deps) : Result<VerifyResult, string> =
        match deps.ReadLocalManifest () with
        | Error e -> Error e
        | Ok None -> Error "no .eru/manifest.json found. Run 'eru manifest init' first."
        | Ok (Some manifest) ->
            let missing =
                manifest.Files
                |> List.filter (fun f -> deps.ResolveLocalGlob f.Path |> List.isEmpty)
                |> List.map (fun f -> f.Path)
            Ok { Total = manifest.Files.Length; Missing = missing }
