module Eru.Serve.SiteServeServer

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.FileProviders
open Eru
open Eru.Adapters
open Eru.Search
open Eru.Site

type ServeOptions = {
    OutputDir    : string
    Port         : int
    OpenBrowser  : bool
    SyncInterval : int
}

module ServeOptions =
    let defaults = {
        OutputDir    = "./cache-site/"
        Port         = 5173
        OpenBrowser  = true
        SyncInterval = 15
    }

let private jsonOpts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

// Bumped on every successful regeneration; polled by the browser (js/app.js) to
// trigger a page reload without holding a persistent per-tab connection open (an
// EventSource-per-tab design exhausts the browser's ~6 connections-per-origin cap
// once a few documents are open at once).
let mutable private siteVersion = 1

let private bumpVersion () = Threading.Interlocked.Increment(&siteVersion) |> ignore

// Wraps an HttpContext handler returning Task<unit> into a RequestDelegate (HttpContext -> Task).
let private rd (f: HttpContext -> Task<unit>) : RequestDelegate =
    RequestDelegate(fun ctx -> f ctx :> Task)

let run (deps: Deps) (cfg: EffectiveConfig) (opts: ServeOptions) : Task<int> =
    task {
        let absOutputDir = Path.GetFullPath opts.OutputDir
        let genOpts = { SiteGenerator.GenerateOptions.defaults with OutputDir = opts.OutputDir }

        // 1. Initial generate — fail fast on error
        match SiteGenerator.generate deps cfg genOpts with
        | Error e ->
            eprintfn "[eru serve] Error generating site: %s" e
            return 1
        | Ok () ->

        // 2. Build WebApplication
        let app = WebApplication.Create()

        app.UseStaticFiles(StaticFileOptions(
            FileProvider = new PhysicalFileProvider(absOutputDir),
            RequestPath  = PathString.Empty)) |> ignore

        app.MapGet("/", RequestDelegate(fun (ctx: HttpContext) ->
            ctx.Response.Redirect("/index.html")
            Task.CompletedTask)) |> ignore

        app.MapGet("/api/search", rd (fun ctx -> task {
            let q     = ctx.Request.Query["q"].ToString().ToLowerInvariant()
            let terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> Array.toList
            let backend : SearchFn =
                match Environment.GetEnvironmentVariable "ERU_SEARCH_BACKEND" with
                | "indexed" -> IndexedSearch.search
                | "ck"      -> CkSearch.search
                | _         -> SimpleScan.search
            let candidates = CandidateBuilder.build deps cfg (deps.GetCwd())
            let hits        = backend terms candidates
            let searchHits =
                hits |> List.map (fun (f, excerpts) ->
                    // Use "{sourceName}:{remotePath}" format to match card.dataset.id in the browser
                    let path =
                        match f.RemotePath, f.SourceName with
                        | Some rp, Some sn -> $"{sn}:{rp}"
                        | _ -> f.RelPath
                    {   Path        = path
                        Source      = match f.Source with Cache -> "cache" | Lock -> "lock" | Local -> "local"
                        SourceName  = f.SourceName
                        Tags        = f.Tags
                        Description = f.Description
                        Excerpts    = excerpts })
                |> List.toArray
            ctx.Response.ContentType <- "application/json"
            do! ctx.Response.WriteAsync(JsonSerializer.Serialize({ Hits = searchHits }, jsonOpts))
        })) |> ignore

        app.MapGet("/api/sync", RequestDelegate(fun (ctx: HttpContext) ->
            ctx.Response.StatusCode <- 202
            Task.Run(fun () ->
                try
                    Sync.populateIndex deps |> ignore
                    match SiteGenerator.generate deps cfg genOpts with
                    | Ok ()  -> bumpVersion ()
                    | Error e -> eprintfn "[eru serve] Sync regeneration failed: %s" e
                with ex -> eprintfn "[eru serve] Sync error: %s" ex.Message) |> ignore
            Task.CompletedTask)) |> ignore

        app.MapGet("/api/version", rd (fun ctx -> task {
            ctx.Response.ContentType <- "application/json"
            do! ctx.Response.WriteAsync(JsonSerializer.Serialize({| version = siteVersion |}, jsonOpts))
        })) |> ignore

        // 3. Start background sync loop
        let cts = new CancellationTokenSource()
        Task.Run(fun () ->
            task {
                use timer = new PeriodicTimer(TimeSpan.FromMinutes(float opts.SyncInterval))
                let mutable keepGoing = true
                while keepGoing do
                    try
                        let! ticked = timer.WaitForNextTickAsync(cts.Token)
                        if ticked then
                            try
                                Sync.populateIndex deps |> ignore
                                match SiteGenerator.generate deps cfg genOpts with
                                | Ok ()  -> bumpVersion ()
                                | Error e -> eprintfn "[eru serve] Background regen failed: %s" e
                            with ex -> eprintfn "[eru serve] Background sync error: %s" ex.Message
                        else
                            keepGoing <- false
                    with :? OperationCanceledException ->
                        keepGoing <- false
            } :> Task) |> ignore

        // 4. Open browser
        if opts.OpenBrowser then
            try
                let psi = Diagnostics.ProcessStartInfo($"http://localhost:{opts.Port}", UseShellExecute = true)
                Diagnostics.Process.Start(psi) |> ignore
            with _ -> ()

        // 5. Run until stopped
        printfn "[eru serve] Serving at http://localhost:%d  (Ctrl+C to stop)" opts.Port
        do! app.RunAsync($"http://localhost:{opts.Port}")
        cts.Cancel()
        return 0
    }
