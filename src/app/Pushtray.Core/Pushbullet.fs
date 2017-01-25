module Pushtray.Pushbullet

open FSharp.Data
open FSharp.Data.JsonExtensions
open Pushtray.Utils

type UserSchema = JsonProvider<"""../../../schemas/user.json""">
type DevicesSchema = JsonProvider<"""../../../schemas/devices.json""">

module Endpoints =
  let stream accessToken = sprintf "wss://stream.pushbullet.com/websocket/%s" accessToken
  let user = "https://api.pushbullet.com/v2/users/me"
  let devices = "https://api.pushbullet.com/v2/devices"
  let ephemerals = "https://api.pushbullet.com/v2/ephemerals"

let private accessToken = Cli.requiredArg "<access-token>"
let private encryptPass = Cli.arg "<encrypt-pass>"

let user =
  Http.get accessToken Endpoints.user
  |> Option.bind (fun result ->
    try Some <| UserSchema.Parse(result)
    with ex -> Logger.error ex.Message; None)
  |> function
  | Some user -> user
  | None ->
    Logger.fatal "Could not retrieve user info"
    exit 1

let devices =
  Http.get accessToken Endpoints.devices
  |> Option.bind (fun result ->
    try Some <| DevicesSchema.Parse(result).Devices
    with ex -> Logger.error ex.Message; None)
  |> function
  | Some devices -> devices
  | None ->
    Logger.fatal "Could not retrieve user devices"
    exit 1

let private devicesMap =
  devices
  |> Array.map (fun d -> (d.Iden, d))
  |> Map.ofArray

let device iden =
  devicesMap.TryFind iden
  |> Option.orElse (fun _ ->
    Logger.error (sprintf "Unable to find device with iden '%s'" iden)
    None)

module Crypto =
  open System
  open Org.BouncyCastle
  open Org.BouncyCastle.Crypto

  let private iterations = 30000

  let decrypt (password: string) (ciphertext: string) =
    let gen = Generators.Pkcs5S2ParametersGenerator(Digests.Sha256Digest())
    gen.Init
      ( Text.Encoding.UTF8.GetBytes(password),   // Password
        Text.Encoding.ASCII.GetBytes(user.Iden), // Salt
        iterations )

    let bytes = Convert.FromBase64String(ciphertext)
    let version = bytes.[0]
    let tag = bytes.[1..16]
    let iv = bytes.[17..28]
    let message = bytes.[29..]

    try
      let cipher = Security.CipherUtilities.GetCipher("AES/GCM/NoPadding")
      cipher.Init(false, Parameters.ParametersWithIV(gen.GenerateDerivedParameters("AES", 256), iv))
      cipher.DoFinal(Array.append message tag)
      |> Text.Encoding.ASCII.GetString
      |> Some
    with ex ->
      Logger.debug <| sprintf "Pushbullet: Decryption failure (%s)" ex.Message
      None

module Ephemeral =
  open Notification

  type MirrorSchema = JsonProvider<"""../../../schemas/mirror.json""", SampleIsList=true>
  type DismissalSchema = JsonProvider<"""../../../schemas/dismissal.json""", SampleIsList=true>
  type SmsChangedSchema = JsonProvider<"""../../../schemas/sms-changed.json""", InferTypesFromValues=false>

  type EphemeralType =
    | Mirror
    | Dismissal
    | SmsChanged
    | Unknown

  let private typeFromJsonString json =
    try
      match JsonValue.Parse(json)?``type``.AsString() with
      | "mirror" -> Mirror
      | "dismissal" -> Dismissal
      | "sms_changed" -> SmsChanged
      | str ->
        Logger.debug <| sprintf "Unknown push type=%s %s" str json
        Unknown
    with ex ->
      Logger.error <| sprintf "Failed to detect push type (%s)" ex.Message
      Unknown

  let private deviceInfo deviceIden =
    device deviceIden |> Option.map (fun d -> d.Nickname.Trim())

  let dismiss notificationId notificationTag packageName sourceUserIden =
    let ephemeral =
      DismissalSchema.Root
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

  let private handleMirror (push: MirrorSchema.Root) =
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
            Some <| fun _ ->
              dismiss
                push.NotificationId
                push.NotificationTag.JsonValue
                push.PackageName
                push.SourceUserIden
          else None }

  let private handleDismissal (push: DismissalSchema.Root) =
    // TODO
    Logger.trace <| sprintf "Pushbullet: Dismissal %s" push.PackageName
    ()

  let private handleSmsChanged (push: SmsChangedSchema.Root) =
    push.Notifications
    |> Array.iter (fun pushNotif ->
      Logger.trace <| sprintf "Pushbullet: Timestamp %s" ((unixTimeStampToDateTime pushNotif.Timestamp).ToString())
      Notification.send
        { Summary = Text(sprintf "%s" <| pushNotif.Title.Trim())
          Body = Text(pushNotif.Body.Trim())
          DeviceInfo = deviceInfo push.SourceDeviceIden
          Timestamp = Some <| (unixTimeStampToDateTime pushNotif.Timestamp).ToString("hh:mm tt")
          Icon = Notification.Stock("smartphone-symbolic")
          Actions = [||]
          Dismissible = None })

  let handle json =
    Logger.trace <| sprintf "Json: %s" json
    match typeFromJsonString <| json with
    | Mirror -> handleMirror <| MirrorSchema.Parse(json)
    | Dismissal -> handleDismissal <| DismissalSchema.Parse(json)
    | SmsChanged -> handleSmsChanged <| SmsChangedSchema.Parse(json)
    | Unknown -> ()

module Stream =
  open System.Threading
  open System.Timers
  open FSharp.Data.JsonExtensions
  open WebSocketSharp

  type StreamSchema = JsonProvider<"""../../../schemas/stream.json""", SampleIsList=true>

  let private handleEphemeral (push: StreamSchema.Push option) =
    match push with
    | Some p ->
      match p.JsonValue.TryGetProperty("encrypted") with
      | Some e when e.AsBoolean() -> Crypto.decrypt (defaultArg encryptPass "") p.Ciphertext |> Option.iter Ephemeral.handle
      | _ -> Ephemeral.handle <| p.JsonValue.ToString()
    | None -> Logger.debug "Ephemeral message received with no contents"

  let private handleNop (heartbeat: Timer) =
    heartbeat.Stop()
    heartbeat.Start()

  let private handleMessage heartbeat json =
    try
      Logger.trace <| sprintf "Pushbullet: Message[Raw] %s" json
      let message = StreamSchema.Parse(json)
      match message.Type with
      | "push" -> handleEphemeral message.Push
      | "nop" -> handleNop heartbeat
      | t -> Logger.debug <| sprintf "Pushbullet: Message[Unknown] type=%s" t
    with ex ->
      Logger.debug <| sprintf "Failed to handle message (%s)" ex.Message

  let private handleHeartbeatMissed (websocket: WebSocket) reconnect =
    reconnect()

  let rec connect() =
    Logger.trace "Pushbullet: Printing devices"
    devices |> Array.iter (fun d -> Logger.info <| sprintf "Device [%s %s] %s" d.Manufacturer d.Model d.Nickname)

    let streamUrl = Endpoints.stream accessToken
    Logger.info <| sprintf "Connecting to stream %s" streamUrl
    let websocket = new WebSocket(streamUrl)

    let reconnect = fun _ ->
      Logger.trace "Pushbullet: Closing stream connection"
      lock websocket (fun _ -> try websocket.Close(CloseStatusCode.Normal) with ex -> Logger.debug ex.Message)
      Logger.trace "Pushbullet: Reconnecting"
      connect()

    // After 95 seconds of no activity (3 missed nops) we'll assume we need to restart
    let heartbeatTimer =
      fun _ -> handleHeartbeatMissed websocket reconnect
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
