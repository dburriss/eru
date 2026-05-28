namespace Eru

module Init =

    type Command = { Force: bool; Path: string option; IsGlobal: bool }

    let private scaffold = """{
  "version": 1,
  "sources": [],
  "collections": [],
  "settings": null
}
"""

    let private emptyGlobal : GlobalConfig =
        { Version = 1; DefaultSources = []; Collections = []; Defaults = None }

    let run (deps: Deps) (cmd: Command) : int =
        if cmd.IsGlobal && cmd.Path.IsSome then
            eprintfn "Error: --global and a path are mutually exclusive."
            1
        elif cmd.IsGlobal then
            if not cmd.Force then
                match deps.ReadGlobalConfig() with
                | Error e           -> eprintfn "Error: %s" e; 1
                | Ok (Some _)       -> eprintfn "Global config already exists. Use --force to overwrite."; 1
                | Ok None           ->
                    match deps.WriteGlobalConfig emptyGlobal with
                    | Ok ()   -> printfn "Initialized global eru config."; 0
                    | Error e -> eprintfn "Error: %s" e; 1
            else
                match deps.WriteGlobalConfig emptyGlobal with
                | Ok ()   -> printfn "Initialized global eru config."; 0
                | Error e -> eprintfn "Error: %s" e; 1
        else
            let dir        = cmd.Path |> Option.defaultValue (deps.GetCwd())
            let configPath = System.IO.Path.Combine(dir, ".eru", "config.json")

            if System.IO.File.Exists configPath && not cmd.Force then
                eprintfn ".eru/config.json already exists. Use --force to overwrite."
                1
            else
                match deps.WriteLocalFile configPath scaffold with
                | Ok ()   -> printfn "Initialized .eru/config.json in %s" dir; 0
                | Error e -> eprintfn "Error: %s" e; 1
