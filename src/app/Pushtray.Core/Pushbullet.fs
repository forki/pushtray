module Pushtray.Pushbullet

open FSharp.Data
open FSharp.Data.JsonExtensions
open Pushtray.Utils

type User = JsonProvider<"""../../../schemas/user.json""">
type Devices = JsonProvider<"""../../../schemas/devices.json""">

module Endpoints =
  let stream accessToken = sprintf "wss://stream.pushbullet.com/websocket/%s" accessToken
  let user = "https://api.pushbullet.com/v2/users/me"
  let devices = "https://api.pushbullet.com/v2/devices"
  let ephemerals = "https://api.pushbullet.com/v2/ephemerals"

let private accessToken = Cli.requiredArg "<access-token>"
let private encryptPass = Cli.argAsString "<encrypt-pass>"
let private ignoredSmsNumbers = Cli.argAsSet "--ignore-sms"

let user =
  Http.get accessToken Endpoints.user
  |> Option.bind (tryParseJson User.Parse)
  |> function
  | Some user -> user
  | None ->
    Logger.fatal "Could not retrieve user info"
    exit 1

let devices =
  Http.get accessToken Endpoints.devices
  |> Option.bind (tryParseJson Devices.Parse)
  |> function
  | Some d -> d.Devices
  | None ->
    Logger.fatal "Could not retrieve user devices"
    exit 1

let private deviceMap =
  devices
  |> Array.map (fun d -> (d.Iden, d))
  |> Map.ofArray

module Ephemeral =
  open Notification

  type Mirror = JsonProvider<"""../../../schemas/mirror.json""", SampleIsList=true>
  type Dismissal = JsonProvider<"""../../../schemas/dismissal.json""", SampleIsList=true>
  type SmsChanged = JsonProvider<"""../../../schemas/sms-changed.json""", InferTypesFromValues=false>

  let private deviceInfo deviceIden =
    deviceMap.TryFind deviceIden |> Option.map (fun d -> d.Nickname.Trim())

  let dismiss notificationId notificationTag packageName sourceUserIden =
    let ephemeral =
      Dismissal.Root
        ( ``type`` = "dismissal",
          sourceDeviceIden = "",
          sourceUserIden = sourceUserIden,
          packageName = packageName,
          notificationId = notificationId,
          notificationTag = notificationTag )
    ephemeral.JsonValue.ToString()
    |> sprintf """{"push": %s, "type": "push"}"""
    |> Http.post accessToken Endpoints.ephemerals
    |> Async.choice Some

  let private handleAction triggerKey =
    // TODO
    ()

  let private handleMirror (push: Mirror.Root) =
    Notification.send
      { Summary = Text(sprintf "%s: %s" (push.ApplicationName.Trim()) (push.Title.Trim()))
        Body = Text(push.Body.Trim())
        DeviceInfo = deviceInfo push.SourceDeviceIden
        Timestamp = None
        Icon = Notification.Base64(push.Icon)
        Actions = push.Actions |> Array.map (fun a ->
          { Label = a.Label
            Handler = fun _ -> handleAction a.TriggerKey })
        Dismissible =
          if push.Dismissible then
            Some <| fun () ->
              dismiss
                push.NotificationId
                push.NotificationTag.JsonValue
                push.PackageName
                push.SourceUserIden
          else None }

  let private handleDismissal (push: Dismissal.Root) =
    // TODO
    Logger.trace <| sprintf "Pushbullet: Dismissal %s" push.PackageName
    ()

  let private handleSmsChanged (push: SmsChanged.Root) =
    if not <| ignoredSmsNumbers.Contains("*") then
      push.Notifications
      |> Array.filter (fun notif -> not <| ignoredSmsNumbers.Contains(notif.Title.Trim()))
      |> Array.iter (fun notif ->
        Logger.trace <| sprintf "Pushbullet: Timestamp %s" ((unixTimeStampToDateTime notif.Timestamp).ToString())
        Notification.send
          { Summary = Text(sprintf "%s" <| notif.Title.Trim())
            Body = Text(notif.Body.Trim())
            DeviceInfo = deviceInfo push.SourceDeviceIden
            Timestamp = Some <| (unixTimeStampToDateTime notif.Timestamp).ToString("hh:mm tt")
            Icon = Notification.Stock("smartphone-symbolic")
            Actions = [||]
            Dismissible = None })

  let handle json =
    Logger.trace <| sprintf "Json: %s" json
    try
      match JsonValue.Parse(json)?``type``.AsString() with
        | "mirror" -> handleMirror <| Mirror.Parse(json)
        | "dismissal" -> handleDismissal <| Dismissal.Parse(json)
        | "sms_changed" -> handleSmsChanged <| SmsChanged.Parse(json)
        | t ->
          Logger.debug <| sprintf "Unknown push type=%s" t
    with ex ->
      Logger.error <| sprintf "Failed to detect push type (%s)" ex.Message

module Stream =
  open System.Threading
  open System.Timers
  open FSharp.Data.JsonExtensions
  open WebSocketSharp
  open Notification

  type Stream = JsonProvider<"""../../../schemas/stream.json""", SampleIsList=true>

  let private handleEphemeral (push: Stream.Push option) =
    match push with
    | Some p ->
      match p.JsonValue.TryGetProperty("encrypted") with
      | Some e when e.AsBoolean() ->
        match Crypto.decrypt (defaultArg encryptPass "") user.Iden p.Ciphertext with
        | Some eph -> Ephemeral.handle eph
        | None -> Crypto.notifyDecryptionFailure()
      | _ -> Ephemeral.handle <| p.JsonValue.ToString()
    | None -> Logger.debug "Ephemeral message received with no contents"

  let private handleNop (heartbeat: Timer) =
    heartbeat.Stop()
    heartbeat.Start()

  let private handleMessage heartbeat json =
    try
      Logger.trace <| sprintf "Pushbullet: Message[Raw] %s" json
      let message = Stream.Parse(json)
      match message.Type with
      | "push" -> handleEphemeral message.Push
      | "nop" -> handleNop heartbeat
      | t -> Logger.debug <| sprintf "Pushbullet: Message[Unknown] type=%s" t
    with ex ->
      Logger.debug <| sprintf "Failed to handle message (%s)" ex.Message

  let rec connect() =
    Logger.trace "Pushbullet: Printing devices"
    devices |> Array.iter (fun d -> Logger.info <| sprintf "Device [%s %s] %s" d.Manufacturer d.Model d.Nickname)

    let streamUrl = Endpoints.stream accessToken
    Logger.info <| sprintf "Connecting to stream %s" streamUrl
    let websocket = new WebSocket(streamUrl)

    let reconnect = fun () ->
      Logger.trace "Pushbullet: Closing stream connection"
      lock websocket (fun () -> try websocket.Close(CloseStatusCode.Normal) with ex -> Logger.debug ex.Message)
      Logger.trace "Pushbullet: Reconnecting"
      connect()

    // After 95 seconds of no activity (3 missed nops) we'll assume we need to reconnect
    let heartbeatTimer =
      fun _ -> reconnect()
      |> createTimer 95000.0

    websocket.OnMessage.Add(fun e -> handleMessage heartbeatTimer e.Data)
    websocket.OnError.Add(fun e -> Logger.error e.Message)
    websocket.OnOpen.Add(fun _ -> Logger.trace "Pushbullet: Opening stream connection")
    websocket.OnClose.Add (fun e ->
      Logger.debug <| sprintf "Pushbullet: Stream connection closed [Code %d]" e.Code
      match LanguagePrimitives.EnumOfValue<uint16, CloseStatusCode> e.Code with
      | CloseStatusCode.Normal | CloseStatusCode.Away -> ()
      | _ ->
        Logger.trace "Pushbullet: Attempting to reconnect in 5 seconds"
        (createTimer 5000.0 (fun _ -> reconnect())).Start())

    websocket.ConnectAsync()
