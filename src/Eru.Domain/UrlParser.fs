namespace Eru

module UrlParser =

    open System

    type ParsedProviderUrl = {
        RepoUrl    : string
        Branch     : string
        RemotePath : string
        SourceName : string
    }

    let private deriveNameFromUrl (url: string) : string =
        let segment = url.TrimEnd('/').Split([| '/'; ':' |]) |> Array.last
        if segment.EndsWith(".git") then segment.[..segment.Length - 5]
        else segment

    // https://github.com/owner/repo/blob/branch/path...
    let private parseGitHub (uri: Uri) : ParsedProviderUrl option =
        let parts = uri.AbsolutePath.TrimStart('/').Split('/')
        if parts.Length < 5 || parts.[2] <> "blob" then None
        else
            let owner      = parts.[0]
            let repo       = parts.[1]
            let branch     = parts.[3]
            let remotePath = parts.[4..] |> String.concat "/"
            let repoUrl    = $"https://github.com/{owner}/{repo}"
            Some { RepoUrl = repoUrl; Branch = branch; RemotePath = remotePath; SourceName = deriveNameFromUrl repoUrl }

    // https://gitlab.com/owner/repo/-/blob/branch/path...
    let private parseGitLab (uri: Uri) : ParsedProviderUrl option =
        let parts = uri.AbsolutePath.TrimStart('/').Split('/')
        if parts.Length < 6 || parts.[2] <> "-" || parts.[3] <> "blob" then None
        else
            let owner      = parts.[0]
            let repo       = parts.[1]
            let branch     = parts.[4]
            let remotePath = parts.[5..] |> String.concat "/"
            let repoUrl    = $"https://gitlab.com/{owner}/{repo}"
            Some { RepoUrl = repoUrl; Branch = branch; RemotePath = remotePath; SourceName = deriveNameFromUrl repoUrl }

    let private providers : (string * (Uri -> ParsedProviderUrl option)) list = [
        "github.com", parseGitHub
        "gitlab.com", parseGitLab
    ]

    // Returns None if the string is not a recognised provider URL.
    let tryParse (raw: string) : ParsedProviderUrl option =
        if not (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) then
            None
        else
            try
                let uri = Uri(raw)
                providers
                |> List.tryPick (fun (host, parse) ->
                    if uri.Host = host then parse uri else None)
            with _ -> None
