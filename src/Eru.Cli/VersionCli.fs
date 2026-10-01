module Eru.Cli.VersionCli

open System.Reflection
open Argu

let (|VersionCmd|_|) (r: ParseResults<EruArgs>) =
    if r.Contains EruArgs.Version then Some () else None

/// Splits an informational version such as "0.8.0+abc123" into (version, commit).
let parseVersion (informational: string) : string * string option =
    match informational.IndexOf '+' with
    | -1 -> informational, None
    | i  ->
        let commit = informational.Substring(i + 1)
        informational.Substring(0, i), (if commit = "" then None else Some commit)

let run () : int =
    let info =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        |> Option.ofObj
        |> Option.map (fun a -> a.InformationalVersion)
        |> Option.defaultValue "unknown"
    let version, commit = parseVersion info
    printfn "%s" version
    printfn "commit: %s" (defaultArg commit "unknown")
    0
