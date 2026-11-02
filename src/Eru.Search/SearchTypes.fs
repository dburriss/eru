namespace Eru.Search

type KnowledgeSource = Cache | Lock | Local

type CandidateFile = {
    AbsPath     : string
    RelPath     : string
    RemotePath  : string option  // original remote path (for Cache/Lock); None for Local
    Source      : KnowledgeSource
    SourceName  : string option
    Tags        : string list
    Description : string option
}

type SearchHit = {
    Path        : string
    Source      : string
    SourceName  : string option
    Tags        : string list
    Description : string option
    Excerpts    : string list
    /// Relevance within this result only (scales differ per mode); 0 for metadata-only hits.
    Score       : float
}

type SearchResult = {
    /// Best first.
    Hits      : SearchHit[]
    /// True when more files matched than the limit allowed.
    Truncated : bool
    /// Why a result may differ from what was asked for, e.g. semantic search was unavailable.
    Warnings  : string[]
}
