module Contextive.LanguageServer.Tests.Helpers.TestClient

open System
open System.Threading.Tasks
open System.IO.Pipelines
open OmniSharp.Extensions.LanguageProtocol.Testing
open OmniSharp.Extensions.LanguageServer.Client
open OmniSharp.Extensions.JsonRpc.Testing
open Contextive.LanguageServer.Server

type ClientOptionsBuilder = LanguageClientOptions -> LanguageClientOptions

type InitializationOptions =
    | SimpleTestClient
    | TestClient of ClientOptionsBuilder list
    | TestClientWithCustomInitWait of ClientOptionsBuilder list * string option

type private TestClient() =
    inherit LanguageServerTestBase(JsonRpcTestOptions())

    override _.SetupServer() =
        let clientPipe = Pipe()
        let serverPipe = Pipe()

        setupAndStartLanguageServer (serverPipe.Reader.AsStream()) (clientPipe.Writer.AsStream())
        |> ignore

        (clientPipe.Reader.AsStream(), serverPipe.Writer.AsStream())

    member _.Initialize clientOptsBuilder =
        match clientOptsBuilder with
        | Some b -> base.InitializeClient(Action<LanguageClientOptions>(b >> ignore))
        | _ -> base.InitializeClient(null)

let private createTestClient clientOptsBuilder =
    task {
        let testClient = new TestClient()

        let! client = testClient.Initialize(clientOptsBuilder)

        return client, None
    }

let private initAndWaitForConfigLoaded testClientConfig (loadMessage: string option) =
    task {
        let logAwaiter = ConditionAwaiter.create ()

        let allBuilders = ServerLog.optionsBuilder logAwaiter :: testClientConfig

        let clientOptionsBuilder = List.fold (>>) id allBuilders

        let! (client, _) = Some clientOptionsBuilder |> createTestClient

        let! reply =
            (defaultArg loadMessage "Loading contextive")
            |> ServerLog.waitForLogMessage logAwaiter

        return (client, reply)
    }

let initWithReply initOptions =
    match initOptions with
    | SimpleTestClient -> createTestClient None
    | TestClient(testClientConfig) -> initAndWaitForConfigLoaded testClientConfig None
    | TestClientWithCustomInitWait(testClientConfig, loadMessage) ->
        initAndWaitForConfigLoaded testClientConfig loadMessage

let init o =
    task {
        let! result = initWithReply o
        return fst result
    }
