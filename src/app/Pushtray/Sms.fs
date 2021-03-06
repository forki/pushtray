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

module Pushtray.Sms

open System.Text.RegularExpressions
open FSharp.Data
open Pushtray.Pushbullet
open Pushtray.Utils

let [<Literal>] private SendSmsSample = SampleDir + "send-sms.json"
type SmsRequest = JsonProvider<SendSmsSample>

let private sendRequest account targetDeviceIden phoneNumber message =
  let ephemeral =
    SmsRequest.Root
      ( ``type`` = "messaging_extension_reply",
        packageName = "com.pushbullet.android",
        sourceUserIden = account.User.Iden,
        targetDeviceIden = targetDeviceIden,
        conversationIden = phoneNumber,
        message = message )
  Ephemeral.send account <| ephemeral.JsonValue.ToString()

let private selectDevice (devices: Device[]) =
  let numDevices = Array.length devices
  let rec readNumber shouldShowMessage =
    if shouldShowMessage then printf "Please enter a number [1 - %d]: " numDevices
    try
      match int <| System.Console.ReadLine().Trim() with
      | n when n >= 1 && n <= numDevices -> n
      | _ -> readNumber true
    with _ ->
      readNumber true
  if numDevices > 1 then
    for (i, d) in Seq.indexed devices do
      printfn "%d: %s %s" (i + 1) d.Manufacturer d.Nickname
    printf "Choose device [1 - %d]: " numDevices
    devices.[(readNumber false) - 1]
  else if numDevices = 1 then
    devices.[0]
  else
    Logger.fatal "No SMS-capable devices found."
    exit 1

let send (account: AccountData) deviceRegex number message =
  let isSmsCapable (device: Device) = device.Type = "android"
  let device =
    deviceRegex
    |> Option.map (fun regex ->
      account.Devices |> Array.filter (fun d ->
        let doesMatch = Regex.Match(d.Nickname, regex).Success
        if doesMatch && not <| isSmsCapable d then
          Logger.warn <| sprintf "Device '%s' matched but it's not SMS-capable" d.Nickname
        doesMatch && isSmsCapable d))
    |> function
    | Some d when d.Length >= 1 -> selectDevice d
    | _ -> account.Devices |> Array.filter isSmsCapable |> selectDevice
  let response =
    sendRequest account device.Iden number message
    |> Option.bind Async.RunSynchronously
  match response with
  | Some _ -> printfn "SMS sent."
  | None -> Logger.error "Could not send SMS."
