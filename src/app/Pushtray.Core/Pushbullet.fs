module Pushtray.Pushbullet

open FSharp.Data
open FSharp.Data.JsonExtensions
open Pushtray.Cli
open Pushtray.Config
open Pushtray.Utils

type User = JsonProvider<"""../../../schemas/user.json""">
type Devices = JsonProvider<"""../../../schemas/devices.json""">
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
      Logger.fatal <| sprintf "Did you create a config file at '%s'?" userConfigDir
      exit 1

  let request endpoint parse =
    Http.get accessToken endpoint
    |> Option.bind (tryParseJson parse)
  let user =
    match request Endpoints.user User.Parse with
    | Some v -> v
    | None ->
      Logger.fatal "Could not retrieve user info."
      exit 1
  let devices =
    match request Endpoints.devices Devices.Parse with
    | Some v -> v.Devices
    | None ->
      Logger.fatal "Could not retrieve user devices."
      exit 1

  let encryptPass =
    match options.EncryptPass with
    | None -> config |> Option.bind (fun c -> c.EncryptPass)
    | pass -> pass

  { User = user
    Devices = devices
    AccessToken = accessToken
    EncryptPass = encryptPass }

module Ephemeral =
  open Notification

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
            Icon = Notification.Stock("smartphone-symbolic")
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

module Stream =
  open System.Threading
  open System.Timers
  open FSharp.Data.JsonExtensions
  open WebSocketSharp
  open Notification

  type Stream = JsonProvider<"""../../../schemas/stream.json""", SampleIsList=true>

  let private handleEphemeral account (push: Stream.Push option) =
    match push with
    | Some p ->
      match p.JsonValue.TryGetProperty("encrypted") with
      | Some e when e.AsBoolean() ->
        Crypto.decrypt (defaultArg account.EncryptPass "") account.User.Iden p.Ciphertext
        |> Option.iter (Ephemeral.handle account)
      | _ -> Ephemeral.handle account <| p.JsonValue.ToString()
    | None -> Logger.debug "Ephemeral message received with no contents"

  let private handleNop (heartbeat: Timer) =
    heartbeat.Stop()
    heartbeat.Start()

  let private handleMessage account heartbeat json =
    try
      Logger.trace <| sprintf "Pushbullet: Message[Raw] %s" json
      let message = Stream.Parse(json)
      match message.Type with
      | "push" -> handleEphemeral account message.Push
      | "nop" -> handleNop heartbeat
      | t -> Logger.debug <| sprintf "Pushbullet: Message[Unknown] type=%s" t
    with ex ->
      Logger.debug <| sprintf "Failed to handle message (%s)" ex.Message

  let rec connect options =
    Logger.trace "Pushbullet: Retrieving account info..."
    let account = requestAccountData options

    Logger.trace "Pushbullet: Printing devices"
    account.Devices |> Array.iter (fun d ->
      Logger.info <| sprintf "Device [%s %s] %s" d.Manufacturer d.Model d.Nickname)

    let websocket = new WebSocket(Endpoints.stream account.AccessToken)

    let reconnect() =
      Logger.trace "Pushbullet: Closing stream connection"
      lock websocket (fun () ->
        try websocket.Close(CloseStatusCode.Normal) with ex -> Logger.debug ex.Message)
      Logger.trace "Pushbullet: Reconnecting"
      connect options

    // After 95 seconds of no activity (3 missed nops) we'll assume we need to reconnect
    let heartbeatTimer =
      fun _ -> reconnect()
      |> createTimer 95000.0

    websocket.OnMessage.Add(fun e -> handleMessage account heartbeatTimer e.Data)
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
