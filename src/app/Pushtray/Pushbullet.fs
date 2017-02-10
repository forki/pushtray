module Pushtray.Pushbullet

open FSharp.Data
open FSharp.Data.JsonExtensions
open Pushtray.Cli
open Pushtray.Config
open Pushtray.TrayIcon
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
      Logger.fatal <| sprintf "Create a config file %s/config?" userConfigDir
      exit 1

  let request endpoint parse =
    Http.get accessToken endpoint
    |> Option.bind (tryParseJson parse)

  let rec retrieveOrRetry attempts func =
    match func() with
    | Some v -> v
    | None ->
      if attempts > 0 then
        Logger.warn "Could not retrieve user data, retrying in 60 seconds..."
        System.Threading.Thread.Sleep(60000)
        retrieveOrRetry (attempts - 1) func
      else
        Logger.fatal "Could not retrieve required data, exiting..."
        exit 1

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

module Stream =
  open System.Threading
  open System.Timers
  open FSharp.Data.JsonExtensions
  open WebSocketSharp
  open Notification

  type Stream = JsonProvider<"""../../../schemas/stream.json""", SampleIsList=true>

  type Heartbeat(reconnect: unit -> unit, trayIcon: TrayIcon option) =
    // After 95 seconds of no activity (3 missed nops) we'll assume we need to reconnect
    let timer =
      (fun _ ->
        trayIcon |> Option.iter (fun t -> t.ShowSyncing())
        reconnect())
      |> createTimer 95000.0

    do timer.Enabled <- true

    member this.OnNop() =
      trayIcon |> Option.iter (fun t -> t.ShowConnected())
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

  let rec connect (trayIcon: TrayIcon option) options =
    trayIcon |> Option.iter (fun t -> t.ShowSyncing())

    Logger.trace "Pushbullet: Retrieving account info..."
    let account = requestAccountData options
    account.Devices |> Array.iter (fun d ->
      Logger.info <| sprintf "Device [%s %s] %s" d.Manufacturer d.Model d.Nickname)

    let websocket = new WebSocket(Endpoints.stream account.AccessToken)

    let reconnect() =
      lock websocket (fun () ->
        Logger.trace "Pushbullet: Closing stream connection"
        try websocket.Close(CloseStatusCode.Normal) with ex -> Logger.debug ex.Message)
      Logger.trace "Pushbullet: Reconnecting"
      connect trayIcon options

    let heartbeat = new Heartbeat(reconnect, trayIcon)

    websocket.OnMessage.Add(fun e -> handleMessage account heartbeat e.Data)
    websocket.OnError.Add(fun e -> Logger.error e.Message)
    websocket.OnOpen.Add(fun _ -> Logger.trace "Pushbullet: Opening stream connection")
    websocket.OnClose.Add (fun e ->
      Logger.debug <| sprintf "Pushbullet: Stream connection closed [Code %d]" e.Code
      match LanguagePrimitives.EnumOfValue<uint16, CloseStatusCode> e.Code with
      | CloseStatusCode.Normal | CloseStatusCode.Away -> ()
      | _ ->
        Logger.trace "Pushbullet: Websocket closed abnormally, exiting..."
        exit 1)

    websocket.ConnectAsync()
