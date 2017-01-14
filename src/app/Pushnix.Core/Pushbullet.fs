module Pushnix.Pushbullet

open System.Threading
open FSharp.Data
open WebSocketSharp
open Pushnix.Utils

type UserSchema = JsonProvider<"""../../../schemas/user.json""">
type StreamSchema = JsonProvider<"""../../../schemas/stream.json""", SampleIsList = true>
type PushMirrorSchema = JsonProvider<"""../../../schemas/push-mirror.json""">
type PushDismissalSchema = JsonProvider<"""../../../schemas/push-dismissal.json""">

module Endpoints =
  let user = "https://api.pushbullet.com/v2/users/me"
  let stream accessToken = sprintf "wss://stream.pushbullet.com/websocket/%s" accessToken

let accessToken = Cli.requiredArg "<access-token>"

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
    let salt = Text.Encoding.ASCII.GetBytes(user.Iden)
    let gen = Generators.Pkcs5S2ParametersGenerator(Digests.Sha256Digest())
    gen.Init(Text.Encoding.UTF8.GetBytes(password), salt, iterations)

    let bytes = Convert.FromBase64String(ciphertext)
    let version = bytes.[0]
    let tag = bytes.[1..16]
    let iv = bytes.[17..28]
    let message = bytes.[29..]

    let cipher = Security.CipherUtilities.GetCipher("AES/GCM/NoPadding")
    cipher.Init(false, Parameters.ParametersWithIV(gen.GenerateDerivedParameters("AES", 256), iv))
    Text.Encoding.ASCII.GetString <| cipher.DoFinal(Array.append message tag)

let handlePush push =
  () // TODO

let handleMessage password json =
  try
    let message = StreamSchema.Parse(json)
    match message.Type with
    | "push" ->
      match message.Push with
      | Some push ->
        if push.Encrypted then handlePush <| Crypto.decrypt password push.Ciphertext
        else Logger.warn "Received unencrypted push"
      | None -> Logger.error "Push message received with no contents"
    | t -> Logger.trace <| sprintf "Message: type=%s" t
  with ex ->
    Logger.error <| sprintf "%s" ex.Message

let connect password =
  let ws = new WebSocket(Endpoints.stream accessToken)
  ws.OnMessage.Add(fun e -> handleMessage password e.Data)
  ws.OnError.Add(fun e -> Logger.error e.Message)
  ws.Connect()

let start passwordOption =
  match passwordOption with
  | Some p -> connect p
  | None ->
    Logger.fatal <| sprintf "Plaintext access is currently not supported, exiting..."
    exit 1

  let waitEvent = new AutoResetEvent(false)
  waitEvent.WaitOne() |> ignore
