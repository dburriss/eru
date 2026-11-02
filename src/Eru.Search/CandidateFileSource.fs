namespace Eru.Search

open System
open System.IO
open System.Threading
open Ferret.FileSource

/// Hands Ferret exactly the files eru wants searched, instead of a directory tree: cached
/// source files, lock-file entries and `knowledge/` files live in unrelated places. The
/// candidate list is swapped before each search or index build; Ferret keeps its indexes
/// keyed by path, so the list should be the full, stable set (filter results afterwards).
type CandidateFileSource(virtualRoot: string) =
    let mutable candidates : string[] = [||]

    // A NUL byte in the first 8000 bytes marks a binary file, as ripgrep (and Ferret) do.
    let isBinary (path: string) =
        try
            use s = File.OpenRead path
            let buf = Array.zeroCreate<byte> 8000
            let n = s.Read(buf, 0, buf.Length)
            Array.exists ((=) 0uy) buf.[0 .. n - 1]
        with _ -> true

    /// The single directory Ferret is asked to search; it never exists on disk.
    member _.Root = virtualRoot

    /// Replaces the files the next enumeration returns (ordinal path order).
    member _.Set(files: string seq) =
        let sorted = files |> Seq.distinct |> Seq.toArray
        Array.sortInPlaceWith (fun (a: string) b -> String.CompareOrdinal(a, b)) sorted
        Volatile.Write(&candidates, sorted)

    interface IFileSource with
        member _.Kind path =
            if path = virtualRoot then IsDirectory
            elif File.Exists path then IsFile
            else Missing

        member _.Enumerate root =
            if root = virtualRoot then Volatile.Read(&candidates) |> Seq.ofArray else Seq.empty

        member _.ReadLines path =
            if isBinary path then None
            else
                try Some (File.ReadAllLines path |> Seq.ofArray)
                with _ -> None

        member _.Stamp path =
            try
                let fi = FileInfo path
                Some $"{fi.Length}-{fi.LastWriteTimeUtc.Ticks}"
            with _ -> None

        member _.ReadStdin() = Seq.empty
