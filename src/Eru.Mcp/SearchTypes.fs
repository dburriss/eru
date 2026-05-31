namespace Eru.Mcp

type KnowledgeSource = Cache | Lock | Local

type CandidateFile = {
    AbsPath     : string
    RelPath     : string
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
