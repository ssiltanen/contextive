module Contextive.LanguageServer.Definitions

open DotNet.Globbing

open Contextive.Core.File
open Contextive.Core.Definitions
open System.Threading.Tasks

type FileLoader = unit -> Task<Result<File, string>>

type Reloader = unit -> unit
type Unregisterer = unit -> unit

type private State =
    { WorkspaceFolder: string option
      Logger: (string -> unit)
      DefinitionsFilePath: string option
      Definitions: Definitions
      FileLoader: FileLoader
      RegisterWatchedFile: (string option -> Unregisterer) option
      UnregisterLastWatchedFile: Unregisterer option
      OnErrorLoading: (string -> unit) }

    static member Initial() =
        { WorkspaceFolder = None
          Logger = fun _ -> ()
          DefinitionsFilePath = None
          FileLoader = fun _ -> Task.FromResult(Error("Path not yet defined."))
          Definitions = Definitions.Default
          RegisterWatchedFile = None
          UnregisterLastWatchedFile = None
          OnErrorLoading = fun _ -> () }

type LoadReply = string option

type InitPayload =
    { Logger: (string -> unit)
      FileLoader: FileLoader
      RegisterWatchedFile: (string option -> Unregisterer) option
      OnErrorLoading: (string -> unit) }

type AddFolderPayload = { WorkspaceFolder: string option }

type FindReplyChannel = AsyncReplyChannel<FindResult>

type FindPayload =
    { OpenFileUri: string
      Filter: Filter
      ReplyChannel: FindReplyChannel }

type Message =
    | Init of InitPayload
    | Load
    | Find of FindPayload

let private loadFile (state: State) (file: Result<File, string>) =
    match file with
    | Error(e) -> Error(e)
    | Ok({ AbsolutePath = ap
           Contents = contents }) ->
        state.Logger $"Loading contextive from {ap}..."

        match contents with
        | Ok(c) -> deserialize c
        | Error(e) -> Error(e)

module private Handle =
    let init state (initMsg: InitPayload) : State =
        { state with
            Logger = initMsg.Logger
            RegisterWatchedFile = initMsg.RegisterWatchedFile
            OnErrorLoading = initMsg.OnErrorLoading
            FileLoader = initMsg.FileLoader }

    let updateFileWatchers (state: State) (file: Result<File, string>) =
        match state.DefinitionsFilePath, file with
        | Some existingPath, Ok({ AbsolutePath = newPath }) when existingPath = newPath -> state
        | _, Ok({ AbsolutePath = newPath }) ->
            match state.UnregisterLastWatchedFile with
            | Some unregister -> unregister ()
            | None -> ()

            let path = Some newPath

            let unregisterer =
                match state.RegisterWatchedFile with
                | Some rwf -> rwf path |> Some
                | None -> None

            { state with
                UnregisterLastWatchedFile = unregisterer
                DefinitionsFilePath = path }
        | _, _ -> state

    let load (state: State) : Task<State> =
        task {
            let! file = state.FileLoader()

            let defs = loadFile state file

            let newState =
                match defs with
                | Ok defs ->
                    state.Logger "Succesfully loaded."
                    { state with Definitions = defs }
                | Error msg ->
                    let msg = $"Error loading definitions: {msg}"
                    Serilog.Log.Logger.Error msg
                    state.Logger msg
                    state.OnErrorLoading msg

                    { state with
                        Definitions = Definitions.Default }

            return updateFileWatchers newState file
        }

    let matchPreciseGlob (openFileUri: string) pathGlob =
        Glob.Parse(pathGlob).IsMatch(openFileUri)

    let matchGlob (openFileUri: string) pathGlob =
        let formats: Printf.StringFormat<string -> string> list =
            [ "%s"; "**%s"; "**%s*"; "**%s**" ]

        formats
        |> List.map (fun s -> sprintf s pathGlob)
        |> List.exists (matchPreciseGlob openFileUri)

    let matchGlobs openFileUri (context: Context) =
        let matchOpenFileUri = matchGlob openFileUri

        match context.Paths with
        | null -> true
        | _ -> context.Paths |> Seq.exists matchOpenFileUri

    let find (state: State) (findMsg: FindPayload) : State =
        let matchOpenFileUri = matchGlobs findMsg.OpenFileUri

        let foundContexts =
            state.Definitions.Contexts |> Seq.filter matchOpenFileUri |> findMsg.Filter

        findMsg.ReplyChannel.Reply foundContexts
        state

let private handleMessage (state: State) (msg: Message) =
    match msg with
    | Init(initMsg) -> Handle.init state initMsg |> Task.FromResult
    | Load -> Handle.load state
    | Find(findMsg) -> Handle.find state findMsg |> Task.FromResult



let create () =
    MailboxProcessor.Start(fun inbox ->
        let rec loop (state: State) =
            task {
                let! (msg: Message) = inbox.Receive()

                let! newState =
                    try
                        handleMessage state msg
                    with e ->
                        printfn "%A" e
                        state.Logger <| e.ToString()
                        Task.FromResult state

                return! loop newState
            }

        State.Initial() |> loop |> Async.AwaitTask)

let init (definitionsManager: MailboxProcessor<Message>) logger fileLoader registerWatchedFile onErrorLoading =
    Init(
        { Logger = logger
          FileLoader = fileLoader
          RegisterWatchedFile = registerWatchedFile
          OnErrorLoading = onErrorLoading }
    )
    |> definitionsManager.Post

let loader (definitionsManager: MailboxProcessor<Message>) =
    fun () -> Load |> definitionsManager.Post

let find (definitionsManager: MailboxProcessor<Message>) (openFileUri: string) (filter: Filter) =
    let msgBuilder =
        fun rc ->
            Find(
                { OpenFileUri = openFileUri
                  Filter = filter
                  ReplyChannel = rc }
            )

    definitionsManager.PostAndAsyncReply(msgBuilder, 1000) |> Async.StartAsTask
