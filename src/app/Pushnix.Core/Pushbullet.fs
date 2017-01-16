module Pushnix.Pushbullet

open FSharp.Data
open FSharp.Data.JsonExtensions
open WebSocketSharp
open Pushnix.Utils

type UserSchema = JsonProvider<"""../../../schemas/user.json""">
type StreamSchema = JsonProvider<"""../../../schemas/stream.json""", SampleIsList = true>
type PushMirrorSchema = JsonProvider<"""../../../schemas/push-mirror.json""">
type PushDismissalSchema = JsonProvider<"""../../../schemas/push-dismissal.json""">

module Endpoints =
  let user = "https://api.pushbullet.com/v2/users/me"
  let stream accessToken = sprintf "wss://stream.pushbullet.com/websocket/%s" accessToken

let private accessToken = Cli.requiredArg "<access-token>"

let user =
  let userOption =
    Endpoints.user
    |> Http.get accessToken
    |> Option.bind (fun result ->
      try Some <| UserSchema.Parse(result)
      with ex -> Logger.error <| sprintf "%s" ex.Message; None)
  match userOption with
  | Some user -> user
  | None ->
    Logger.fatal "Unable to retrieve user info"
    exit 1

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
    let rec wrap lineSpace words =
      match words with
      | head :: tail ->
        let (acc, length) =
          if String.length head > lineSpace then (head + "\n", width)
          else (head + " ", lineSpace - head.Length)
        acc + (wrap length tail)
      | _ ->  ""
    text.Split(' ')
    |> List.ofArray
    |> wrap width

  let show summary body base64Icon =
    let wrapWidth = 35
    Gtk.Application.Invoke(fun _ _ ->
      let notifn =
        new Notification
          ( lineWrap wrapWidth summary,
            lineWrap wrapWidth body,
            new Pixbuf(Convert.FromBase64String(base64Icon)) )
      notifn.Show())

module private Push =
  type PushType =
    | Mirror
    | Dismissal
    | Unknown

  let private typeFromJsonString json =
    try
      match JsonValue.Parse(json)?``type``.AsString() with
      | "mirror" -> Mirror
      | "dismissal" -> Dismissal
      | str ->
        Logger.warn <| sprintf "Unknown push type '%s'" str
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
    // TODO: Add device information
    Notification.show
      (sprintf "%s: %s" push.ApplicationName push.Title)
      push.Body
      push.Icon

  let private dismissal (push: PushDismissalSchema.Root) =
    // TODO
    ()

  let handle json =
    Logger.trace <| sprintf "JSON: %s" json
    match typeFromJsonString <| json with
    | Mirror -> mirror <| PushMirrorSchema.Parse(json)
    | Dismissal -> dismissal <| PushDismissalSchema.Parse(json)
    | _ -> ()

let private handleMessage password json =
  try
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
    Logger.error <| sprintf "%s" ex.Message

let connect password =
  Logger.trace <| sprintf "Connecting to stream %s" (Endpoints.stream accessToken)
  let ws = new WebSocket(Endpoints.stream accessToken)
  ws.OnMessage.Add(fun e -> handleMessage password e.Data)
  ws.OnError.Add(fun e -> Logger.error e.Message)
  ws.Connect()
