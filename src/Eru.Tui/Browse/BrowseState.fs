module Eru.Tui.Browse.BrowseState

open Eru

type ActiveTab = FilesTab | SourcesTab | LocalTab | CollectionsTab | ConfigTab

[<AbstractClass>]
type SourceTreeNode(label: string) =
    member _.Label = label
    override _.ToString() = label

type SourceNode(src: SourceList.SourceRow) =
    inherit SourceTreeNode(src.Name)
    member _.Source = src

type FileNode(row: SourceFiles.SourceFileRow, sourceName: string, lockEntry: LockEntry option, isGlobAllTracked: bool) =
    inherit SourceTreeNode(row.Path)
    member _.Row = row
    member _.SourceName = sourceName
    member val LockEntry: LockEntry option = lockEntry with get, set
    member this.IsTracked = this.LockEntry.IsSome || isGlobAllTracked

type GlobNode(fileNode: FileNode) =
    inherit SourceTreeNode(fileNode.Row.Path)
    member _.FileNode = fileNode
    member _.SourceName = fileNode.SourceName
    member _.IsTracked = fileNode.IsTracked

type BrowseAction =
    | AddFile       of sourceName: string * remotePath: string
    | AddSource
    | RefreshSource
    | Disconnect    of localPath: string
    | RemoveEntry   of localPath: string
    | RemoveSource  of sourceName: string
