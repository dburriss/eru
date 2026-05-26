namespace Eru

module Init =

    type Command = { Force: bool }

    let private scaffold = """{
  "version": 1,
  "sources": [],
  "settings": null
}
"""

    let run (deps: Deps) (cmd: Command) : int =
        let cwd        = deps.GetCwd()
        let configPath = System.IO.Path.Combine(cwd, "eru.json")

        if System.IO.File.Exists configPath && not cmd.Force then
            eprintfn "eru.json already exists. Use --force to overwrite."
            1
        else
            match deps.WriteLocalFile configPath scaffold with
            | Ok ()  -> printfn "Initialized eru.json in %s" cwd; 0
            | Error e -> eprintfn "Error: %s" e; 1
