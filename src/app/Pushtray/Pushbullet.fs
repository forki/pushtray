// Copyright (c) 2017 Jatan Patel
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

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
      quitApplication 1

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
      [ "\nCreate your access token and then either:"
        "1. Supply it with the --access-token option."
        sprintf "2. Create a config file at %s/config with the following line:" Environment.userConfigDir
        "   access_token = <token>" ]
      |> List.iter (fun s -> System.Console.WriteLine(s))
      quitApplication 1
  let request endpoint parse =
    Http.get accessToken endpoint
    |> Option.bind (tryParseJson parse)
  { User =
      (fun () -> request Endpoints.user User.Parse)
      |> retrieveOrRetry 5
    Devices =
      (fun () ->
         request Endpoints.devices Devices.Parse
         |> Option.map (fun v -> v.Devices))
      |> retrieveOrRetry 5
    AccessToken = accessToken
    EncryptPass =
      match options.EncryptPass with
      | None -> config |> Option.bind (fun c -> c.EncryptPass)
      | pass -> pass }
