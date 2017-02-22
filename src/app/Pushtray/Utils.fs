module Pushtray.Utils

open System.Diagnostics
open System.Timers
open FSharp.Data
open FSharp.Data.HttpRequestHeaders

let [<Literal>] SampleDir = __SOURCE_DIRECTORY__ + "/../../../schemas/"

module Option =
  let getOrElse defaultValue opt =
    match opt with
    | Some v -> v
    | None -> defaultValue

module String =
  let split separators (str: string) = str.Split(separators)

  let stripMargin (str: string) =
    [ for line in split [|'\n'|] str -> line.TrimStart [|' '|] ]
    |> String.concat "\n"

module Async =
  let map f v =
    async {
      let! value = v
      return f value
    }

  let choice onSuccess asyncChoice =
    asyncChoice |> map (fun choice ->
      match choice with
      | Choice1Of2 result -> onSuccess result
      | Choice2Of2 (ex: exn) ->
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

let unixTimeStampToDateTime (timestamp: decimal) =
  System.DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(float timestamp)

let createTimer interval func =
  let timer = new Timer(interval)
  timer.Elapsed.Add(func)
  timer.AutoReset <- false
  timer

let quitApplication code =
  Gtk.Application.Quit()
  exit code
