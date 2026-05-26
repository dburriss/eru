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

    let create () : Deps =
        let cwd = Directory.GetCurrentDirectory()
        {
            ReadGlobalConfig   = ConfigAdapter.readGlobalConfig
            ReadLocalConfig    = fun () -> ConfigAdapter.readLocalConfig cwd
            ReadLockEntries    = LockFileAdapter.read
            WriteLockEntries   = LockFileAdapter.write
            FetchRemoteContent = fun _url _branch _path -> Error "git fetch not yet implemented"
            WriteLocalFile     = writeFile
            HashContent        = hashContent
            GetCwd             = fun () -> cwd
        }
