module Pushtray.Utils

open System
open System.IO
open System.Diagnostics
open System.Timers
open FSharp.Data
open FSharp.Data.HttpRequestHeaders

let [<Literal>] SampleDir = __SOURCE_DIRECTORY__ + "/../../../samples/"

module Option =
  let getOrElse defaultValue (opt: 'a option) =
    match opt with
    | Some v -> v
    | None -> defaultValue

module String =
  let split separators (str: string) =
    str.Split(separators)

module Async =
  let map f v =
    async {
      let! value = v
      return f value
    }

  let choice onSuccess (asyncChoice: Async<Choice<string, exn>>) =
    asyncChoice |> map (fun choice ->
      match choice with
      | Choice1Of2 result -> onSuccess result
      | Choice2Of2 (ex: Exception) ->
        Logger.debug <| sprintf "AsyncChoice: %s" ex.Message
        None)

module Http =
  let private tokenHeader accessToken =
    "Access-Token", accessToken

  let get accessToken url =
    Logger.trace <| sprintf "Request(GET): %s" url
    try
      Http.RequestString
        ( url,
          headers = [ Accept HttpContentTypes.Json; tokenHeader accessToken ],
          timeout = 5000 )
      |> Some
    with ex ->
      Logger.error <| sprintf "Request(GET): %s (%s)" ex.Message url
      None

  let getAsync accessToken url =
    Logger.trace <| sprintf "RequestAsync(GET): %s" url
    Http.AsyncRequestString
      ( url,
        headers = [ Accept HttpContentTypes.Json; tokenHeader accessToken ],
        timeout = 5000 )
    |> Async.Catch

  let post accessToken url body =
    Logger.trace <| sprintf "RequestAsync(POST): %s (Body = %s)" url body
    Http.AsyncRequestString
      ( url,
        headers = [ ContentType HttpContentTypes.Json; tokenHeader accessToken ],
        body = TextRequest body,
        timeout = 5000 )
    |> Async.Catch

let tryParseJson parse result =
  try Some <| parse(result)
  with ex -> Logger.error ex.Message; None

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

let userConfigDir =
  Path.Combine
    [| Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
       "pushtray" |]

let userConfigFile =
  let filePath = Path.Combine(userConfigDir, "config")
  if File.Exists(filePath) then Some filePath else None

let unixTimeStampToDateTime (timestamp: decimal) =
  System.DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(float timestamp)

let createTimer interval func =
  let timer = new Timer(interval)
  timer.Elapsed.Add(func)
  timer.AutoReset <- false
  timer
