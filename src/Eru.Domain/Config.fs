namespace Eru

type SourceConfig = {
    Name: string
    Url: string option
    Branch: string option
    Prefix: string option
}

type CollectionFileRef = {
    Source: string
    RemotePath: string
    Tags: string list
}

type CollectionConfig = {
    Name: string
    Tags: string list
    Files: CollectionFileRef list
}

type GlobalDefaults = {
    Branch: string option
    CommitOnPull: bool option
}

type GlobalConfig = {
    Version: int
    DefaultSources: SourceConfig list
    Collections: CollectionConfig list
    Defaults: GlobalDefaults option
}

type LocalSettings = {
    CommitOnPull: bool option
    StateFile: string option
}

type LocalConfig = {
    Version: int
    Sources: SourceConfig list
    Settings: LocalSettings option
}

type EffectiveConfig = {
    Sources: SourceConfig list
    CommitOnPull: bool
    StateFile: string
}

module Config =
    let merge (globalCfg: GlobalConfig option) (localCfg: LocalConfig option) : EffectiveConfig =
        let globalSources = globalCfg |> Option.map (fun g -> g.DefaultSources) |> Option.defaultValue []
        let localSources  = localCfg  |> Option.map (fun l -> l.Sources)        |> Option.defaultValue []

        let localWithUrls, localInherited = localSources |> List.partition (fun s -> s.Url.IsSome)

        let inherited =
            localInherited
            |> List.choose (fun ls ->
                globalSources |> List.tryFind (fun gs -> gs.Name = ls.Name))

        let globalOnly =
            globalSources
            |> List.filter (fun gs ->
                localSources |> List.forall (fun ls -> ls.Name <> gs.Name))

        let mergedSources = localWithUrls @ inherited @ globalOnly

        let globalCommitOnPull =
            globalCfg
            |> Option.bind (fun g -> g.Defaults)
            |> Option.bind (fun d -> d.CommitOnPull)
            |> Option.defaultValue false

        let localCommitOnPull =
            localCfg
            |> Option.bind (fun l -> l.Settings)
            |> Option.bind (fun s -> s.CommitOnPull)

        let stateFile =
            localCfg
            |> Option.bind (fun l -> l.Settings)
            |> Option.bind (fun s -> s.StateFile)
            |> Option.defaultValue "eru.lock"

        {
            Sources = mergedSources
            CommitOnPull = localCommitOnPull |> Option.defaultValue globalCommitOnPull
            StateFile = stateFile
        }

    let resolveByTags (tags: string list) (globalCfg: GlobalConfig) : (string * string) list =
        let normalised = tags |> List.map (fun t -> t.ToLowerInvariant())
        let hasAllTags (itemTags: string list) =
            normalised |> List.forall (fun t -> itemTags |> List.exists (fun it -> it.ToLowerInvariant() = t))

        globalCfg.Collections
        |> List.collect (fun col ->
            let colMatches = hasAllTags col.Tags
            col.Files
            |> List.choose (fun f ->
                if colMatches || hasAllTags f.Tags then Some (f.Source, f.RemotePath)
                else None))
        |> List.distinct
