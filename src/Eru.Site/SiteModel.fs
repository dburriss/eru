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
    Status      : FileStatus
    Body        : string option
    PageUrl     : string option
}

type SiteSource = {
    Name        : string
    Url         : string option
    HasManifest : bool
    FileCount   : int
    Files       : SiteDocument list
}

type SiteTag = {
    Name      : string
    FileCount : int
    Files     : SiteDocument list
}

type SiteModel = {
    Documents     : SiteDocument list
    Sources       : SiteSource list
    Tags          : SiteTag list
    AllExtensions : string list
}
