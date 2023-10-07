module Contextive.LanguageServer.Configuration

open System.Threading.Tasks

let resolvedPathGetter configGetter pathResolver () =
    task {
        let! path = configGetter ()
        return pathResolver path
    }

let handler (definitionsLoader: Definitions.Reloader) _ =
    definitionsLoader ()
    Task.CompletedTask
