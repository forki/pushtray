module Pushtray.Utils

open System
open System.IO
open System.Diagnostics
open FSharp.Data
open FSharp.Data.HttpRequestHeaders

module Option =
  let orElse func (opt: 'a option) =
    match opt with
    | None -> func()
    | v -> v

module String =
  let split separators (str: string) =
    str.Split(separators)

module Async =
  let map f v =
    async {
      let! value = v
      return f value
    }

  let choice success (asyncChoice: Async<Choice<string, exn>>) =
    asyncChoice |> map (fun choice ->
      match choice with
      | Choice1Of2 result -> success result
      | Choice2Of2 (ex: Exception) ->
        Logger.debug <| sprintf "AsyncChoice: %s" ex.Message
        None)

module Http =
  let tokenHeader accessToken =
    "Access-Token", accessToken

  let get accessToken url =
    Logger.trace <| sprintf "Request(GET): %s" url
    try
      Http.RequestString
        ( url,
          headers = [ Accept HttpContentTypes.Json; tokenHeader accessToken ] )
      |> Some
    with ex ->
      Logger.error <| sprintf "Request(GET): %s (%s)" ex.Message url
      None

  let getAsync accessToken url =
    Logger.trace <| sprintf "RequestAsync(GET): %s" url
    Http.AsyncRequestString
      ( url,
        headers = [ Accept HttpContentTypes.Json; tokenHeader accessToken ] )
    |> Async.Catch

  let post accessToken url body =
    Logger.trace <| sprintf "RequestAsync(POST): %s (Body = %s)" url body
    Http.AsyncRequestString
      ( url,
        headers = [ ContentType HttpContentTypes.Json; tokenHeader accessToken ],
        body = TextRequest body )
    |> Async.Catch

let appDataDirs =
  try
    let dataDirs =
      [ Environment.SpecialFolder.ApplicationData
        Environment.SpecialFolder.CommonApplicationData ]
      |> List.map (fun p -> Path.Combine(Environment.GetFolderPath(p), "pushtray"))
    Some <| (AppDomain.CurrentDomain.BaseDirectory :: dataDirs)
  with ex ->
    Logger.debug <| sprintf "DataDir: %s" ex.Message
    None

let unixTimeStampToDateTime (timestamp: decimal) =
  System.DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(float timestamp)
