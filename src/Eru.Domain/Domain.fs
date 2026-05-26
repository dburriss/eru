namespace Eru

type ArtifactStatus =
    | Current
    | Drifted
    | Conflicted
    | Missing
    | Pinned

type SyncPolicy =
    | Upstream
    | Contribute
    | Mirror
    | Pin
