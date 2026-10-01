namespace Eru

// Warns when a bundle was auto-detected as `manifest` but has no
// .eru/manifest.json, so it would be registered yet publish nothing.
module BundleKindWarning =

    let private manifestPath (path: string) =
        if path = "" then ".eru/manifest.json" else $"{path}/.eru/manifest.json"

    let noManifestWarning (deps: Deps) (url: string) (branch: string option) (path: string) : string option =
        let actualBranch = branch |> Option.defaultValue "HEAD"
        match deps.FetchRemoteContent url actualBranch [ manifestPath path ] with
        | Ok ((_, content) :: _) when content <> "" -> None
        | _ ->
            let shown = if path = "" then "." else path
            Some (
                $"Warning: bundle '{shown}' was detected as kind 'manifest' but has no {manifestPath path}, " +
                "so it will publish no files. Either:\n" +
                "  - add `okf_version` to the bundle's index.md frontmatter (auto-detected as okf),\n" +
                "  - re-run with `--kind okf`, or\n" +
                "  - create a manifest (`eru manifest init`).")
