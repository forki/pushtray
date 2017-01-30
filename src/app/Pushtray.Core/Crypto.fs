module Pushtray.Crypto

open System
open Org.BouncyCastle
open Org.BouncyCastle.Crypto
open Org.BouncyCastle.Security
open Pushtray.Notification

let private getCipher() =
 Security.CipherUtilities.GetCipher("AES/GCM/NoPadding")

let private generateKey (password: string) (salt: string) =
  let gen = Generators.Pkcs5S2ParametersGenerator(Digests.Sha256Digest())
  gen.Init
    ( Text.Encoding.UTF8.GetBytes(password),   // Password
      Text.Encoding.ASCII.GetBytes(salt), // Salt
      30000 )
  gen.GenerateDerivedParameters("AES", 256)

let private secureRandom = new SecureRandom()

let private initializationVector size =
  let bytes = Array.zeroCreate size
  secureRandom.NextBytes(bytes)
  bytes

let encrypt (password: string) (salt: string) (message: string) =
  let key = generateKey password salt
  let iv = initializationVector 12

  try
    let cipher = getCipher()
    cipher.Init(true, Parameters.ParametersWithIV(key, iv))
    let bytes = cipher.DoFinal(Text.Encoding.UTF8.GetBytes(message))
    let tagStart = bytes.Length - 16
    Array.concat [| [| byte (0x31) |]; bytes.[tagStart..]; iv; bytes.[0..tagStart - 1] |]
    |> Convert.ToBase64String
    |> Some
  with ex ->
    Logger.debug <| sprintf "Pushbullet: Encryption failure (%s)" ex.Message
    Notification.send
      { Summary = Text("Pushtray: Encryption failure")
        Body = Text("Something went wrong! The push couldn't be sent.")
        DeviceInfo = None
        Timestamp = None
        Icon =  Notification.Stock(Gtk.Stock.Info)
        Actions = [||]
        Dismissible = None }
    None

let decrypt (password: string) (salt: string) (ciphertext: string) =
  let key = generateKey password salt
  let bytes = Convert.FromBase64String(ciphertext)
  let version = bytes.[0]
  let tag = bytes.[1..16]
  let iv = bytes.[17..28]
  let message = bytes.[29..]

  try
    let cipher = getCipher()
    cipher.Init(false, Parameters.ParametersWithIV(key, iv))
    cipher.DoFinal(Array.append message tag)
    |> Text.Encoding.ASCII.GetString
    |> Some
  with ex ->
    Logger.debug <| sprintf "Pushbullet: Decryption failure (%s)" ex.Message
    Notification.send
      { Summary = Text("Pushtray: Decryption failure")
        Body = Text("Your password might be incorrect.")
        DeviceInfo = None
        Timestamp = None
        Icon =  Notification.Stock(Gtk.Stock.Info)
        Actions = [||]
        Dismissible = None }
    None
