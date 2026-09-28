namespace Eru.Adapters

// The one genuinely new piece of infrastructure in this repo: spawns a long-lived
// subprocess and talks bidirectional stdio JSON-RPC to it over the Agent Client
// Protocol (agentclientprotocol.com), via the Acp.Net package. Kept entirely inside
// Eru.Adapters — Eru.Domain never references Acp.Net or System.Diagnostics.Process
// directly, only the `RunAgent : AgentConfig -> workingDir -> prompt -> onChunk -> Result<string, string>`
// shape (`Deps.RunAgent`).
//
// Note for anyone touching this: `Connection.ClientSideConnection`'s `Start()` is
// what pumps `Transport.StdioTransport`'s incoming messages and correlates them to
// pending requests — call *only* `connection.Start()` (it starts the transport for
// you); calling `transport.Start()` yourself first throws "Transport is not in
// Created state", and skipping `connection.Start()` entirely leaves every
// `SendRequestAsync` call hanging until it's cancelled, with no error to explain why.

open System
open System.Diagnostics
open System.Text
open System.Threading
open System.Threading.Tasks
open Acp.Net
open Eru

module AcpAgentAdapter =

    let private defaultTurnTimeout = TimeSpan.FromSeconds 120.0

    let run (agent: AgentConfig) (workingDir: string) (prompt: string) (onChunk: string -> unit) : Result<AgentRunResult, string> =
        if agent.Protocol <> "acp" then
            Error $"unsupported agent protocol '{agent.Protocol}' — only 'acp' is supported."
        else

        let turnTimeout =
            agent.Timeout
            |> Option.map (float >> TimeSpan.FromSeconds)
            |> Option.defaultValue defaultTurnTimeout

        let psi = ProcessStartInfo(agent.Command)
        for a in agent.Args do psi.ArgumentList.Add a
        psi.RedirectStandardInput  <- true
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError  <- true
        psi.UseShellExecute        <- false
        psi.WorkingDirectory       <- workingDir

        let mutable proc : Process = null
        try
            try
                proc <- Process.Start psi

                use transport = new Transport.StdioTransport(proc.StandardOutput, proc.StandardInput)
                let connection = Connection.ClientSideConnection(transport)
                connection.Start()

                use cts = new CancellationTokenSource(turnTimeout)

                // `turnTimeout` is an idle timeout, not a total-turn budget: every
                // session/update notification (not just message chunks — a tool call or
                // plan update counts too) is evidence the agent is still working, so it
                // pushes the deadline out another `turnTimeout` rather than letting a
                // single fixed clock run out on a slow-but-alive agent. `CancelAfter` is
                // safe to call after the token has already fired; the try/with below is
                // only for the (rare) case a notification races the `use cts` disposal
                // on the way out of this function.
                let bumpDeadline () = try cts.CancelAfter turnTimeout with :? ObjectDisposedException -> ()

                // Accumulates the agent's final response from `agent_message_chunk`
                // session/update notifications.
                let response = StringBuilder()
                connection.HandleSessionUpdate(fun notification ->
                    bumpDeadline ()
                    match notification.Update with
                    | SessionUpdate.AgentMessageChunk chunk ->
                        match chunk.Content with
                        | ContentBlock.Text textContent ->
                            response.Append(textContent.Text) |> ignore
                            onChunk textContent.Text
                        | _ -> ()
                    | _ -> ()
                    Task.FromResult ())

                let clientCapabilities : ClientCapabilities =
                    { Fs = { ReadTextFile = false; WriteTextFile = false; Meta = None }
                      Terminal = false
                      Session = None
                      Elicitation = None
                      Meta = None }
                let initializeRequest : InitializeRequest =
                    { ProtocolVersion = 1
                      ClientCapabilities = clientCapabilities
                      ClientInfo = Some { Name = "eru"; Title = None; Version = "0.1"; Meta = None }
                      Meta = None }
                let sw = Stopwatch.StartNew()
                connection.InitializeAsync(initializeRequest, cts.Token).GetAwaiter().GetResult() |> ignore
                let initializeMs = sw.Elapsed.TotalMilliseconds

                let sessionRequest : NewSessionRequest =
                    { Cwd = workingDir; AdditionalDirectories = None; McpServers = []; Meta = None }
                sw.Restart()
                let session = connection.SessionNewAsync(sessionRequest, cts.Token).GetAwaiter().GetResult()
                let sessionNewMs = sw.Elapsed.TotalMilliseconds

                let promptRequest : PromptRequest =
                    { SessionId = session.SessionId
                      Prompt = [ ContentBlock.Text { Text = prompt; Annotations = None; Meta = None } ]
                      Meta = None }
                sw.Restart()
                let promptResponse = connection.PromptAsync(promptRequest, cts.Token).GetAwaiter().GetResult()
                let promptMs = sw.Elapsed.TotalMilliseconds

                connection.Close()

                let timings = { InitializeMs = initializeMs; SessionNewMs = sessionNewMs; PromptMs = promptMs }
                match promptResponse.StopReason with
                | StopReason.EndTurn         -> Ok { Response = response.ToString(); Timings = timings }
                | StopReason.Refusal         -> Error "agent refused the request."
                | StopReason.Cancelled       -> Error "agent turn was cancelled."
                | StopReason.MaxTokens       -> Error "agent stopped early: reached its max-tokens limit."
                | StopReason.MaxTurnRequests -> Error "agent stopped early: reached its max-turn-requests limit."
                | _                          -> Error "agent stopped for an unrecognized reason."
            with
            | :? OperationCanceledException -> Error $"agent produced no activity for {turnTimeout.TotalSeconds}s."
            | ex -> Error ex.Message
        finally
            if not (isNull proc) then
                (try if not proc.HasExited then proc.Kill(true) with _ -> ())
                (try proc.Dispose() with _ -> ())
