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

  let choice success errorMessage (asyncChoice: Async<Choice<string, exn>>) =
    asyncChoice |> map (fun choice ->
      match choice with
      | Choice1Of2 result -> success result
      | Choice2Of2 (ex: Exception) -> Logger.debug <| sprintf "AsyncChoice: %s" ex.Message; None
      |> Option.orElse (fun _ -> Logger.error errorMessage; None))

module Http =
  let get accessToken url =
    Logger.trace <| sprintf "Request GET: %s" url
    try
      Http.RequestString
        ( url,
          headers = [ Accept HttpContentTypes.Json; "Access-Token", accessToken ] )
      |> Some
    with ex ->
      Logger.error <| sprintf "Request GET: %s (%s)" ex.Message url
      None

  let getAsync accessToken url =
    Logger.trace <| sprintf "RequestAsync GET: %s" url
    Http.AsyncRequestString
      ( url,
        headers = [ Accept HttpContentTypes.Json; "Access-Token", accessToken ])
    |> Async.Catch

  let post accessToken body url =
    Logger.trace <| sprintf "RequestAsync POST: %s (Body = %s)" url body
    Http.AsyncRequestString
      ( url,
        headers = [ ContentType HttpContentTypes.Json; "Access-Token", accessToken ],
        body = TextRequest body )
    |> Async.Catch

let shellExec (command: string) args =
  let startInfo =
    ProcessStartInfo
      ( FileName = command,
        Arguments = args,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden )
  let proc = new Process(StartInfo = startInfo, EnableRaisingEvents = true)
  async {
    try
      proc.Start() |> ignore
      return proc.ExitCode
    finally
      proc.Dispose()
  }

let appDataDir =
  try
    Environment.GetEnvironmentVariable("XDG_DATA_DIRS").Split(Path.PathSeparator)
    |> Array.map (fun p -> Path.Combine [| p; "pushtray" |])
    |> Array.filter Directory.Exists
    |> Array.head
    |> Some
  with :? System.ArgumentException as ex ->
    Logger.debug <| sprintf "DataDir: %s" ex.Message
    None

let unixTimeStampToDateTime (timestamp: decimal) =
  System.DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(float timestamp)
