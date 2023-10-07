module Contextive.LanguageServer.Tests.FileLoaderTests

open Expecto
open Contextive.LanguageServer
open Swensen.Unquote
open System.IO
open System.Threading.Tasks

[<Tests>]
let fileLoaderTests =
    testList
        "LanguageServer.File Loader Tests"
        [

          testTask "Path is in error state" {
              let pathGetter () = Task.FromResult(Error("No path"))
              let! file = (FileLoader.loader pathGetter) ()
              test <@ file = Error("No path") @>
          }

          testTask "Path Doesn't exist" {
              let path = "/file/not/found"
              let pathGetter () = Task.FromResult(Ok(path))
              let! file = (FileLoader.loader pathGetter) ()

              match file with
              | Error(e) -> failtest e
              | Ok(f: Contextive.Core.File.File) ->
                  test <@ f.AbsolutePath = path @>
                  test <@ f.Contents = Error("Definitions file not found.") @>
          }

          testTask "Path exists" {
              let path =
                  Path.Combine(Directory.GetCurrentDirectory(), "fixtures/completion_tests/two.yml")

              let pathGetter () = Task.FromResult(Ok(path))
              let! file = (FileLoader.loader pathGetter) ()

              match file with
              | Error(e) -> failtest e
              | Ok(f: Contextive.Core.File.File) ->
                  test <@ f.AbsolutePath = path @>

                  test
                      <@
                          f.Contents = Ok(
                              "contexts:
  - terms:
    - name: word1
    - name: word2
    - name: word3"
                          )
                      @>
          } ]
