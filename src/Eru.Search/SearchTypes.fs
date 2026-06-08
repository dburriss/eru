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

type SearchFn = string list -> CandidateFile list -> (CandidateFile * string list) list

type SearchHit = {
    Path        : string
    Source      : string
    SourceName  : string option
    Tags        : string list
    Description : string option
    Excerpts    : string list
}

type SearchResult = {
    Hits : SearchHit[]
}
