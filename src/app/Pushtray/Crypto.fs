// Copyright (c) 2017 Jatan Patel
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

module Pushtray.Crypto

open System
open Org.BouncyCastle
open Org.BouncyCastle.Crypto
open Org.BouncyCastle.Security
open Pushtray.Notification

let private createCipher() = Security.CipherUtilities.GetCipher("AES/GCM/NoPadding")
let private secureRandom = new SecureRandom()

let private generateKey (password: string) (salt: string) =
  let gen = Generators.Pkcs5S2ParametersGenerator(Digests.Sha256Digest())
  gen.Init
    ( Text.Encoding.UTF8.GetBytes(password),
      Text.Encoding.ASCII.GetBytes(salt),
      30000 )
  gen.GenerateDerivedParameters("AES", 256)

let private initializationVector size =
  let bytes = Array.zeroCreate size
  secureRandom.NextBytes(bytes)
  bytes

let private sendInfoNotification summary body =
  Notification.send
    { Summary = Text(summary)
      Body = Text(body)
      Device = None
      Timestamp = None
      Icon =  Notification.Stock(Gtk.Stock.Info)
      Actions = [||]
      Dismissible = None }

let encrypt (password: string) (salt: string) (message: string) =
  let iv = initializationVector 12
  try
    let cipher = createCipher()
    cipher.Init(true, Parameters.ParametersWithIV(generateKey password salt, iv))
    let bytes = cipher.DoFinal(Text.Encoding.UTF8.GetBytes(message))
    let tagStart = bytes.Length - 16
    Array.concat
      [| [| byte (0x31) |]
         bytes.[tagStart..]
         iv
         bytes.[0..tagStart - 1] |]
    |> Convert.ToBase64String
    |> Some
  with ex ->
    Logger.debug <| sprintf "Pushbullet: Encryption failure (%s)" ex.Message
    sendInfoNotification
      "Pushtray: Encryption Failure"
      "Something went wrong! The push couldn't be sent."
    None

let decrypt (password: string) (salt: string) (ciphertext: string) =
  let bytes = Convert.FromBase64String(ciphertext)
  let version = bytes.[0]
  let tag = bytes.[1..16]
  let iv = bytes.[17..28]
  let message = bytes.[29..]
  try
    let cipher = createCipher()
    cipher.Init(false, Parameters.ParametersWithIV(generateKey password salt, iv))
    cipher.DoFinal(Array.append message tag)
    |> Text.Encoding.ASCII.GetString
    |> Some
  with ex ->
    Logger.debug <| sprintf "Pushbullet: Decryption failure (%s)" ex.Message
    sendInfoNotification
      "Pushtray: Decryption Failure"
      "Your password might be incorrect."
    None
