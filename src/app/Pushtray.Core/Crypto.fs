module Pushtray.Crypto

open System
open Org.BouncyCastle
open Org.BouncyCastle.Crypto
open Pushtray.Notification

let private iterations = 30000

let decrypt (password: string) (salt: string) (ciphertext: string) =
  let gen = Generators.Pkcs5S2ParametersGenerator(Digests.Sha256Digest())
  gen.Init
    ( Text.Encoding.UTF8.GetBytes(password),   // Password
      Text.Encoding.ASCII.GetBytes(salt), // Salt
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

let notifyDecryptionFailure() =
  Notification.send
    { Summary = Text("Pushtray: Decryption failure")
      Body = Text("Your password might be incorrect.")
      DeviceInfo = None
      Timestamp = None
      Icon =  Notification.Stock(Gtk.Stock.Info)
      Actions = [||]
      Dismissible = None }
