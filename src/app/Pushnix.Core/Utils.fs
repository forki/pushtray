module Pushnix.Utils

open System
open System.Diagnostics
open FSharp.Data
open FSharp.Data.HttpRequestHeaders

let logOption message opt =
  match opt with
  | None -> Logger.error message
  | _ -> ()
  opt

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
      |> logOption errorMessage)

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
