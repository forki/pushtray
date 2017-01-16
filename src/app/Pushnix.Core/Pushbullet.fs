module Pushnix.Pushbullet

open FSharp.Data
open FSharp.Data.JsonExtensions
open WebSocketSharp
open Pushnix.Utils

type UserSchema = JsonProvider<"""../../../schemas/user.json""">
type DevicesSchema = JsonProvider<"""../../../schemas/devices.json""">
type StreamSchema = JsonProvider<"""../../../schemas/stream.json""", SampleIsList = true>
type PushMirrorSchema = JsonProvider<"""../../../schemas/push-mirror.json""", SampleIsList = true>
type PushDismissalSchema = JsonProvider<"""../../../schemas/push-dismissal.json""", SampleIsList = true>
type PushSmsChangedSchema = JsonProvider<"""../../../schemas/sms-changed.json""">

module Endpoints =
  let stream accessToken = sprintf "wss://stream.pushbullet.com/websocket/%s" accessToken
  let user = "https://api.pushbullet.com/v2/users/me"
  let devices = "https://api.pushbullet.com/v2/devices"

let private accessToken = Cli.requiredArg "<access-token>"

let user =
  Http.get accessToken Endpoints.user
  |> Option.bind (fun result ->
    try Some <| UserSchema.Parse(result)
    with ex -> Logger.error ex.Message; None)
  |> function
  | Some user -> user
  | None -> Logger.fatal "Could not retrieve user info"; exit 1

let devices =
  Http.get accessToken Endpoints.devices
  |> Option.bind (fun result ->
    try Some <| DevicesSchema.Parse(result).Devices
    with ex -> Logger.error ex.Message; None)
  |> function
  | Some devices -> devices
  | None -> Logger.fatal "Could not retrieve user devices"; exit 1

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

module Notification =
  open System
  open Gdk
  open Notifications

  let private lineWrap width (text: string) =
    let rec wrap remaining words =
      match words with
      | head :: tail ->
        let (acc, remain) =
          if String.length head > remaining then (head + "\n", width)
          else (head + " ", remaining - head.Length)
        acc + (wrap remain tail)
      | _ ->  ""
    text.Split('\n')
    |> Array.map (fun line -> line.Split(' ') |> (List.ofArray >> wrap width))
    |> String.concat "\n"

  let private handleAction triggerKey =
    // TODO
    ()

  let show summary body base64Icon (actions: PushMirrorSchema.Action[]) dismissable =
    let wrapWidth = 35
    Gtk.Application.Invoke(fun _ _ ->
      let notif =
        new Notification
          ( lineWrap wrapWidth summary,
            lineWrap wrapWidth body,
            new Pixbuf(Convert.FromBase64String(base64Icon)) )
      actions |> Array.iter (fun a -> notif.AddAction(a.Label, a.Label, fun _ _ -> handleAction a.TriggerKey))
      notif.Show())

module private Push =
  type PushType =
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
        Logger.warn <| sprintf "Unknown push type=%s %s" str json
        Unknown
    with ex ->
      Logger.error <| sprintf "Failed to detect push type (%s)" ex.Message
      Unknown

  let private trace ``type`` (title: string) (body: string) =
    sprintf "Pushbullet: Push [%s]\n\tTitle = %s\n\tBody = %s"
      ``type``
      (title.Trim())
      (body.Trim())
    |> Logger.trace

  let private mirror (push: PushMirrorSchema.Root) =
    trace push.Type push.Title push.Body

    let deviceInfo =
      if Cli.argExists "--show-device" then
        match device push.SourceDeviceIden with
        | Some d -> sprintf "\n\nDevice: %s" (d.Nickname.Trim())
        | None -> ""
      else ""

    Notification.show
      (sprintf "%s: %s" (push.ApplicationName.Trim()) (push.Title.Trim()))
      (sprintf "%s%s" (push.Body.Trim()) deviceInfo)
      push.Icon
      push.Actions
      push.Dismissible

  let private dismissal (push: PushDismissalSchema.Root) =
    // TODO
    ()

  let private smsChanged (push: PushSmsChangedSchema.Root) =
    // TODO
    // Use generic user icon
    ()

  let handle json =
    Logger.trace <| sprintf "Json: %s" json
    match typeFromJsonString <| json with
    | Mirror -> mirror <| PushMirrorSchema.Parse(json)
    | Dismissal -> dismissal <| PushDismissalSchema.Parse(json)
    | SmsChanged -> smsChanged <| PushSmsChangedSchema.Parse(json)
    | Unknown -> ()

let private handleMessage password json =
  try
    Logger.trace <| sprintf "Raw Message: %s" json
    let message = StreamSchema.Parse(json)
    match message.Type with
    | "push" ->
      match message.Push with
      | Some push ->
        if push.Encrypted then Option.iter Push.handle <| Crypto.decrypt password push.Ciphertext
        else Logger.info "Pushbullet: Received unencrypted push"
      | None -> Logger.error "Push message received with no contents"
    | t -> Logger.trace <| sprintf "Message: type=%s" t
  with ex ->
    Logger.warn <| sprintf "Failed to handle message (%s)" ex.Message

let connect password =
  Logger.trace "Printing devices..."
  devices |> Array.iter (fun d -> Logger.trace <| sprintf "Device [%s %s] %s" d.Manufacturer d.Model d.Nickname)

  // Connect to event stream
  Logger.trace <| sprintf "Connecting to stream %s" (Endpoints.stream accessToken)
  let ws = new WebSocket(Endpoints.stream accessToken)
  ws.OnMessage.Add(fun e -> handleMessage password e.Data)
  ws.OnError.Add(fun e -> Logger.error e.Message)
  ws.Connect()
