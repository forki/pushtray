module Pushtray.Pushbullet

open System.Threading
open FSharp.Data
open Pushtray.Cli
open Pushtray.Config
open Pushtray.Utils

let [<Literal>] private UserSample = SampleDir + "user.json"
let [<Literal>] private DevicesSample = SampleDir + "devices.json"

type User = JsonProvider<UserSample>
type Devices = JsonProvider<DevicesSample>
type Device =  Devices.Devicis

module Endpoints =
  let stream accessToken = sprintf "wss://stream.pushbullet.com/websocket/%s" accessToken
  let user = "https://api.pushbullet.com/v2/users/me"
  let devices = "https://api.pushbullet.com/v2/devices"
  let ephemerals = "https://api.pushbullet.com/v2/ephemerals"

type AccountData =
  { User: User.Root
    Devices: Device[]
    AccessToken: string
    EncryptPass: string option }

let rec private retrieveOrRetry attempts func =
  match func() with
  | Some v -> v
  | None ->
    if attempts > 0 then
      Logger.error "Could not retrieve user data, retrying in 60 seconds..."
      Thread.Sleep(60000)
      retrieveOrRetry (attempts - 1) func
    else
      Logger.fatal "Could not retrieve required data, exiting..."
      exit 1

let requestAccountData (options: Cli.Options) =
  let accessToken =
    let opt =
      match options.AccessToken with
      | None -> config |> Option.bind (fun c -> c.AccessToken)
      | token -> token
    match opt with
    | Some token -> token
    | None ->
      Logger.fatal "Access token not provided."
      Logger.fatal <| sprintf "Create a config file %s/config?" userConfigDir
      exit 1

  let request endpoint parse =
    Http.get accessToken endpoint
    |> Option.bind (tryParseJson parse)

  let getUser() =
    let result = request Endpoints.user User.Parse
    result

  let getDevices() =
    let result = request Endpoints.devices Devices.Parse
    result |> Option.map (fun v -> v.Devices)

  let encryptPass =
    match options.EncryptPass with
    | None -> config |> Option.bind (fun c -> c.EncryptPass)
    | pass -> pass

  { User = getUser |> retrieveOrRetry 5
    Devices = getDevices |> retrieveOrRetry 5
    AccessToken = accessToken
    EncryptPass = encryptPass }
