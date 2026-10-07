namespace Eru.Site

type FileStatus = Pulled | Cached | IndexOnly

type SiteDocument = {
    Id          : string
    Source      : string
    RemotePath  : string
    Title       : string
    Extension   : string
    Tags        : string list
    Description : string option
    SyncStatus  : FileStatus
    Body        : string option
    PageUrl     : string option
    Type        : string option
    Status      : string option              // OKF lifecycle: draft|stable|deprecated
    Generated   : Eru.Frontmatter.ActorAt option
    Verified    : Eru.Frontmatter.ActorAt list
    StaleAfter  : System.DateTimeOffset option
    Resource    : string option
    Bundle      : string option              // display name of the most specific covering bundle
}

type SiteSource = {
    Name        : string
    Url         : string option
    Description : string option
    HasManifest : bool
    FileCount   : int
    Files       : SiteDocument list
}

type SiteTag = {
    Name      : string
    FileCount : int
    Files     : SiteDocument list
}

type SiteType = {
    Name      : string
    FileCount : int
    Files     : SiteDocument list
}

type SiteBundle = {
    Name      : string                       // "<source>" for a root bundle, else "<source>/<path>"
    Source    : string
    Path      : string                       // repo-relative; "" for the root bundle
    Kind      : string                       // "okf" | "manifest"
    FileCount : int
    Files     : SiteDocument list
}

type SiteModel = {
    Documents     : SiteDocument list
    Sources       : SiteSource list
    Tags          : SiteTag list
    Types         : SiteType list
    Bundles       : SiteBundle list
    AllExtensions : string list
}

type RelatedDoc = {
    Id         : string
    Title      : string
    PageUrl    : string option
    IsExternal : bool
}

type RelatedLinks = {
    Incoming : RelatedDoc list
    Outgoing : RelatedDoc list
}
