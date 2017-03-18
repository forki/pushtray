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

module Pushtray.Ephemeral

open FSharp.Data
open FSharp.Data.JsonExtensions
open Pushtray.Pushbullet
open Pushtray.Notification
open Pushtray.Cli
open Pushtray.Utils

let [<Literal>] private MirrorSample = SampleDir + "mirror.json"
type Mirror = JsonProvider<MirrorSample, SampleIsList=true>
let [<Literal>] private DismissalSample = SampleDir + "dismissal.json"
type Dismissal = JsonProvider<DismissalSample, SampleIsList=true>
let [<Literal>] private SmsChangedSample = SampleDir + "sms-changed.json"
type SmsChanged = JsonProvider<SmsChangedSample, InferTypesFromValues=false>

let send account pushAsJson =
  let encryptedJson password =
    Crypto.encrypt password account.User.Iden pushAsJson
    |> Option.map (sprintf """{"ciphertext": "%s", "encrypted": true}""")
  account.EncryptPass
  |> Option.fold (fun _ p -> encryptedJson p) (Some pushAsJson)
  |> Option.map
    (sprintf """{"push": %s, "type": "push"}"""
      >> Http.post account.AccessToken Endpoints.ephemerals
      >> Async.choice Some)

let dismiss triggerKey account (push: Mirror.Root) =
  let ephemeral =
    Dismissal.Root
      ( ``type`` = "dismissal",
        sourceDeviceIden = None,
        sourceUserIden = push.SourceUserIden,
        packageName = push.PackageName,
        notificationId = push.NotificationId,
        notificationTag = push.NotificationTag.JsonValue,
        triggerAction = triggerKey )
  ephemeral.JsonValue.ToString()
  |> send account
  |> Option.iter (Async.Ignore >> Async.Start)

let private handleMirror account (push: Mirror.Root) =
  Notification.send
    { Summary = Text(sprintf "%s: %s" (push.ApplicationName.Trim()) (push.Title.Trim()))
      Body = Text(push.Body.Trim())
      Device = account.Devices |> Array.tryFind (fun d -> d.Iden = push.SourceDeviceIden)
      Timestamp = None
      Icon = Notification.Base64(push.Icon)
      Actions =
        push.Actions |> Array.map (fun a ->
          { Label = a.Label
            Handler = fun _ -> dismiss (Some a.TriggerKey) account push })
      Dismissible =
        if push.Dismissible then Some <| fun () -> dismiss None account push
        else None }

let private handleDismissal (push: Dismissal.Root) =
  Logger.trace <| sprintf "Pushbullet: Dismissal %s" push.PackageName

let private handleSmsChanged account (push: SmsChanged.Root) =
  if not <| args.Options.IgnoreSms.Contains("*") then
    for notif in push.Notifications do
      if not <| args.Options.IgnoreSms.Contains(notif.Title.Trim()) then
        Logger.trace <| sprintf "Pushbullet: Timestamp %s" ((unixTimeStampToDateTime notif.Timestamp).ToString())
        Notification.send
          { Summary = Text(sprintf "%s" <| notif.Title.Trim())
            Body = Text(notif.Body.Trim())
            Device = account.Devices |> Array.tryFind (fun d -> d.Iden = push.SourceDeviceIden)
            Timestamp = Some notif.Timestamp
            Icon = Notification.Stock(args.Options.SmsNotifyIcon |> Option.getOrElse "phone")
            Actions = [||]
            Dismissible = None }

let handle account json =
  Logger.trace <| sprintf "Pushbullet: Message[Json] %s" json
  try
    match JsonValue.Parse(json)?``type``.AsString() with
    | "mirror" -> handleMirror account <| Mirror.Parse(json)
    | "dismissal" -> handleDismissal <| Dismissal.Parse(json)
    | "sms_changed" -> handleSmsChanged account <| SmsChanged.Parse(json)
    | t -> Logger.debug <| sprintf "Unknown push type=%s" t
  with ex ->
    Logger.error <| sprintf "Failed to detect push type (%s)" ex.Message
