module Pushtray.Sms

open System.Text.RegularExpressions
open Pushtray.Pushbullet

let private selectDevice (devices: Pushbullet.Device[]) =
  let numDevices = Array.length devices
  let rec readNumber shouldShowMessage =
    if shouldShowMessage then printf "Please enter a number [1 - %d]: " numDevices
    try
      match int <| System.Console.ReadLine().Trim() with
      | n when n >= 1 && n <= numDevices -> n
      | _ -> readNumber true
    with ex ->
      readNumber true
  if numDevices > 1 then
    devices |> Array.iteri (fun i d -> printfn "%d: %s %s" (i + 1) d.Manufacturer d.Nickname)
    printf "Choose device [1 - %d]: " numDevices
    devices.[(readNumber false) - 1]
  else
    devices.[0]

let send deviceRegex number message =
  let isSmsCapable (device: Pushbullet.Device) = device.Type = "android"

  let device =
    deviceRegex
    |> Option.map (fun regex ->
      devices |> Array.filter (fun d ->
        let doesMatch = Regex.Match(d.Nickname, regex).Success
        if doesMatch && not <| isSmsCapable d then
          Logger.warn <| sprintf "Device '%s' matched but it's not SMS-capable" d.Nickname
        doesMatch && isSmsCapable d))
    |> function
    | Some d when d.Length > 1 -> selectDevice d
    | Some d when d.Length = 1 -> Array.head d
    | _ -> devices |> Array.filter isSmsCapable |> selectDevice

  match Ephemeral.sendSms user.Iden device.Iden number message with
  | Some req -> req |> (Async.Ignore >> Async.RunSynchronously)
  | None -> Logger.error "Could not send SMS message"
