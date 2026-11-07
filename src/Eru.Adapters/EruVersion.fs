namespace Eru.Adapters

open System.Reflection

module EruVersion =

    /// The running eru's informational version ("0.15.1" or "0.15.2-alpha+abc123"), read from the
    /// entry assembly (the CLI), or None when it can't be determined (e.g. under a test host).
    let current : string option =
        let asm = Assembly.GetEntryAssembly()
        if isNull asm then None
        else
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            |> Option.ofObj
            |> Option.map (fun a -> a.InformationalVersion)
            |> Option.filter (fun v -> (Eru.SemVer.parse v).IsSome)
