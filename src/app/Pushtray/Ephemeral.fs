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

let private findDevice (devices: Device[]) iden =
  devices |> Array.tryFind (fun d -> d.Iden = iden)

let private handleMirror account (push: Mirror.Root) =
  Notification.send
    { Summary = Text(sprintf "%s: %s" (push.ApplicationName.Trim()) (push.Title.Trim()))
      Body = Text(push.Body.Trim())
      Device = findDevice account.Devices push.SourceDeviceIden
      Timestamp = None
      Icon = Notification.Base64(push.Icon)
      Actions = push.Actions |> Array.map (fun a ->
        { Label = a.Label
          Handler = fun _ -> dismiss (Some a.TriggerKey) account push })
      Dismissible =
        if push.Dismissible then Some <| fun () -> dismiss None account push
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
          Device = findDevice account.Devices push.SourceDeviceIden
          Timestamp = Some notif.Timestamp
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
