namespace Eru.Adapters

open Eru
open System
open System.IO
open System.Security.Cryptography

module AdapterDeps =

    let private hashContent (content: string) : string =
        let bytes = System.Text.Encoding.UTF8.GetBytes content
        let hex   = Convert.ToHexString(SHA256.HashData bytes).ToLowerInvariant()
        $"sha256:{hex}"

    let private writeFile (path: string) (content: string) : Result<unit, string> =
        try
            let dir = Path.GetDirectoryName path
            if dir <> null && dir <> "" then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(path, content)
            Ok ()
        with ex -> Error ex.Message

    let private cacheSourceContent (sourceName: string) (contentHash: string) (content: string) : Result<string, string> =
        try
            let hex = if contentHash.StartsWith "sha256:" then contentHash.[7..] else contentHash
            let dir = Paths.sourceFilesDir sourceName
            Directory.CreateDirectory dir |> ignore
            let filePath = Path.Combine(dir, hex)
            File.WriteAllText(filePath, content)
            Ok $"files/{hex}"
        with ex -> Error ex.Message

    let private readCachedSourceContent (sourceName: string) (cacheRelPath: string) : Result<string option, string> =
        try
            let absPath = Path.Combine(Paths.sourceCacheDir sourceName, cacheRelPath)
            if not (File.Exists absPath) then Ok None
            else Ok (Some (File.ReadAllText absPath))
        with ex -> Error ex.Message

    let create (debug: bool) : Deps =
        let cwd = Directory.GetCurrentDirectory()
        {
            ReadGlobalConfig         = ConfigAdapter.readGlobalConfig
            ReadLocalConfig          = fun () -> ConfigAdapter.readLocalConfig cwd
            WriteLocalConfig         = ConfigAdapter.writeLocalConfig cwd
            WriteGlobalConfig        = ConfigAdapter.writeGlobalConfig
            ReadLockEntries          = fun stateFile ->
                let newPath = Paths.lockFilePath cwd (Some stateFile)
                let oldPath = IO.Path.Combine(cwd, stateFile)
                if not (IO.File.Exists newPath) && IO.File.Exists oldPath then
                    IO.Directory.CreateDirectory(IO.Path.GetDirectoryName newPath) |> ignore
                    IO.File.Move(oldPath, newPath)
                LockFileAdapter.read newPath
            WriteLockEntries         = fun stateFile entries ->
                LockFileAdapter.write (Paths.lockFilePath cwd (Some stateFile)) entries
            FetchRemoteContent       = GitAdapter.fetchRemoteContent debug
            ListRemoteTopLevel       = GitAdapter.listRemoteTopLevel debug
            ListRemoteFiles          = GitAdapter.listRemoteFiles debug
            WriteLocalFile           = writeFile
            ReadLocalFile            = fun path ->
                try
                    if File.Exists path then Ok (Some (File.ReadAllText path))
                    else Ok None
                with ex -> Error ex.Message
            DeleteLocalFile          = fun path -> try File.Delete path; Ok () with ex -> Error ex.Message
            HashContent              = hashContent
            GetCwd                   = fun () -> cwd
            ReadCachedManifest       = ManifestAdapter.readCachedManifest
            CacheSourceManifest      = ManifestAdapter.cacheSourceManifest
            ReadLocalManifest        = fun () -> ManifestAdapter.readLocalManifest cwd
            WriteLocalManifest       = ManifestAdapter.writeLocalManifest cwd
            ResolveLocalGlob         = ManifestAdapter.resolveLocalGlob cwd
            ReadSourceIndex          = SourceIndexAdapter.readIndex
            WriteSourceIndex         = SourceIndexAdapter.writeIndex
            CacheSourceContent       = cacheSourceContent
            ReadCachedSourceContent  = readCachedSourceContent
            BuildSearchIndex         = fun sourceName cacheRelPath ->
                let absPath = Path.Combine(Paths.sourceCacheDir sourceName, cacheRelPath)
                SearchIndexAdapter.getOrBuild absPath |> ignore
        }
