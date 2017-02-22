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

module Pushtray.Stream

open FSharp.Data
open FSharp.Data.JsonExtensions
open WebSocketSharp
open Pushtray.Pushbullet
open Pushtray.Utils

let [<Literal>] private StreamSample = SampleDir + "stream.json"
type Stream = JsonProvider<StreamSample, SampleIsList=true>

type Update =
  { OnConnected: unit -> unit
    OnDisconnected: unit -> unit }

type Heartbeat(reconnect: unit -> unit, update: Update option) =
  // After 95 seconds of no activity (3 missed nops) we'll assume we need to reconnect
  let timer =
    (fun _ ->
      Option.iter (fun v -> v.OnDisconnected()) update
      reconnect())
    |> createTimer 95000.0

  do timer.Enabled <- true

  member this.OnNop() =
    Option.iter (fun v -> v.OnConnected()) update
    timer.Stop()
    timer.Start()

let private handleEphemeral account (push: Stream.Push option) =
  match push with
  | Some p ->
    match p.JsonValue.TryGetProperty("encrypted") with
    | Some e when e.AsBoolean() ->
      Crypto.decrypt (account.EncryptPass |> Option.getOrElse "") account.User.Iden p.Ciphertext
      |> Option.iter (Ephemeral.handle account)
    | _ -> Ephemeral.handle account <| p.JsonValue.ToString()
  | None -> Logger.debug "Ephemeral message received with no contents"

let private handleMessage account (heartbeat: Heartbeat) json =
  try
    Logger.trace <| sprintf "Pushbullet: Message[Raw] %s" json
    let message = Stream.Parse(json)
    match message.Type with
    | "push" -> handleEphemeral account message.Push
    | "nop" -> heartbeat.OnNop()
    | t -> Logger.debug <| sprintf "Pushbullet: Message[Unknown] type=%s" t
  with ex ->
    Logger.debug <| sprintf "Failed to handle message (%s)" ex.Message

let rec connect (update: Update option) options =
  Logger.trace "Pushbullet: Retrieving account info..."
  let account = requestAccountData options
  for d in account.Devices do
    Logger.info <| sprintf "Device [%s %s] %s" d.Manufacturer d.Model d.Nickname

  let websocket = new WebSocket(Endpoints.stream account.AccessToken)
  let reconnect() =
    lock websocket (fun () ->
      try websocket.Close(CloseStatusCode.Normal)
      with ex -> Logger.debug ex.Message)
    connect update options
  let heartbeat = new Heartbeat(reconnect, update)

  websocket.OnMessage.Add(fun e -> handleMessage account heartbeat e.Data)
  websocket.OnError.Add(fun e -> Logger.error e.Message)
  websocket.OnOpen.Add(fun _ -> Logger.trace "Pushbullet: Opening stream connection")
  websocket.OnClose.Add (fun e ->
    Logger.debug <| sprintf "Pushbullet: Stream connection closed [Code %d]" e.Code
    match LanguagePrimitives.EnumOfValue<uint16, CloseStatusCode> e.Code with
    | CloseStatusCode.Normal | CloseStatusCode.Away -> ()
    | _ ->
      Logger.trace "Pushbullet: Websocket closed abnormally, exiting..."
      quitApplication 1)

  websocket.ConnectAsync()
