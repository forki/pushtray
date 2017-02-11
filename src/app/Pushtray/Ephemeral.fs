module Pushtray.Ephemeral

open FSharp.Data
open FSharp.Data.JsonExtensions
open Pushtray.Pushbullet
open Pushtray.Notification
open Pushtray.Cli
open Pushtray.Utils

type Mirror = JsonProvider<"""../../../schemas/mirror.json""", SampleIsList=true>
type Dismissal = JsonProvider<"""../../../schemas/dismissal.json""", SampleIsList=true>
type SmsChanged = JsonProvider<"""../../../schemas/sms-changed.json""", InferTypesFromValues=false>

let private deviceInfo (devices: Device[]) deviceIden =
  devices |> Array.tryFind (fun d -> d.Iden = deviceIden)
  |> Option.map (fun d -> d.Nickname.Trim())

let send account pushJson =
  let encryptedJson password =
    Crypto.encrypt password account.User.Iden pushJson
    |> Option.map (sprintf """{"ciphertext": "%s", "encrypted": true}""")
  account.EncryptPass
  |> Option.fold (fun _ p -> encryptedJson p) (Some pushJson)
  |> Option.map
    (sprintf """{"push": %s, "type": "push"}"""
      >> Http.post account.AccessToken Endpoints.ephemerals
      >> Async.choice Some)

let dismiss userIden (push: Mirror.Root) =
  let ephemeral =
    Dismissal.Root
      ( ``type`` = "dismissal",
        sourceDeviceIden = "",
        sourceUserIden = push.SourceUserIden,
        packageName = push.PackageName,
        notificationId = push.NotificationId,
        notificationTag = push.NotificationTag.JsonValue )
  send userIden <| ephemeral.JsonValue.ToString()

let private handleAction triggerKey =
  // TODO
  ()

let private handleMirror account (push: Mirror.Root) =
  Notification.send
    { Summary = Text(sprintf "%s: %s" (push.ApplicationName.Trim()) (push.Title.Trim()))
      Body = Text(push.Body.Trim())
      DeviceInfo = deviceInfo account.Devices push.SourceDeviceIden
      Timestamp = None
      Icon = Notification.Base64(push.Icon)
      Actions = push.Actions |> Array.map (fun a ->
        { Label = a.Label
          Handler = fun _ -> handleAction a.TriggerKey })
      Dismissible =
        if push.Dismissible then Some <| fun () -> dismiss account push
        else None }

let private handleDismissal (push: Dismissal.Root) =
  Logger.trace <| sprintf "Pushbullet: Dismissal %s" push.PackageName

let private handleSmsChanged account (push: SmsChanged.Root) =
  if not <| args.Options.IgnoreSms.Contains("*") then
    push.Notifications
    |> Array.filter (fun notif -> not <| args.Options.IgnoreSms.Contains(notif.Title.Trim()))
    |> Array.iter (fun notif ->
      Logger.trace <| sprintf "Pushbullet: Timestamp %s" ((unixTimeStampToDateTime notif.Timestamp).ToString())
      Notification.send
        { Summary = Text(sprintf "%s" <| notif.Title.Trim())
          Body = Text(notif.Body.Trim())
          DeviceInfo = deviceInfo account.Devices push.SourceDeviceIden
          Timestamp = Some <| (unixTimeStampToDateTime notif.Timestamp).ToString("hh:mm tt")
          Icon = Notification.Stock(args.Options.SmsNotifyIcon |> Option.getOrElse "phone")
          Actions = [||]
          Dismissible = None })

let handle account json =
  Logger.trace <| sprintf "Pushbullet: Message[Json] %s" json
  try
    match JsonValue.Parse(json)?``type``.AsString() with
      | "mirror" -> handleMirror account <| Mirror.Parse(json)
      | "dismissal" -> handleDismissal <| Dismissal.Parse(json)
      | "sms_changed" -> handleSmsChanged account <| SmsChanged.Parse(json)
      | t ->
        Logger.debug <| sprintf "Unknown push type=%s" t
  with ex ->
    Logger.error <| sprintf "Failed to detect push type (%s)" ex.Message