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

module Pushtray.Notification

open System
open Gdk
open Notifications
open Pushtray.Pushbullet
open Pushtray.Cli
open Pushtray.Utils

type NotificationData =
  { Summary: NotificationText
    Body: NotificationText
    Device: Device option
    Timestamp: decimal option
    Icon: Icon
    Actions: Action[]
    Dismissible: (unit -> unit) option }

and NotificationText =
  | Text of string
  | TextWithFormat of (string * (string -> string))

and Icon =
  | Stock of string
  | Base64 of string
  | File of string

and Action =
  { Label: string
    Handler: ActionArgs -> unit }

type Format =
  | Full
  | Short

let private format =
  match args.Options.NotifyFormat.ToLower() with
  | "full" -> Full
  | "short" -> Short
  | str ->
    Logger.warn <| sprintf "Unknown notify-format value '%s'" str
    Short

let private wrapLine width line =
  let rec loop spaceRemaining result words =
    match words with
    | head :: tail ->
      let (acc, remaining) =
        if String.length head > spaceRemaining then (head + "\n", width)
        else (head + " ", spaceRemaining - head.Length)
      loop remaining (result + acc) tail
    | _ -> result
  String.split [|' '|] line
  |> (List.ofArray >> loop width "")

let private padLine width line =
  (if String.length line < width then
     line + (String.replicate (width - line.Length) " ")
   else
     line)
  |> sprintf "%s%s" "  "

let private prettify text =
  text
  |> String.split [|'\n'|]
  |> Array.collect (wrapLine args.Options.NotifyLineWrap >> String.split [|'\n'|])
  |> Array.map (padLine args.Options.NotifyLinePad)
  |> String.concat "\n"

let private footer data = function
  | Full ->
    let deviceName = data.Device |> Option.fold (fun _ v -> v.Nickname) ""
    let timestamp =
      match data.Timestamp with
      | Some t -> (unixTimeStampToDateTime t).ToString("hh:mm tt")
      | None -> ""
    sprintf "%s %s" deviceName timestamp
    |> prettify
    |> sprintf "\n<i>%s</i>"
  | Short -> ""

let send data =
  let formatText = function
    | Text(str) -> prettify str
    | TextWithFormat(str, format) -> format <| prettify str
  let summary = formatText data.Summary
  let body = formatText data.Body + footer data format

  Gtk.Application.Invoke(fun _ _ ->
    let notification =
      match data.Icon with
      | Stock(str) -> new Notification(summary, body, str)
      | Base64(str) -> new Notification(summary, body, new Pixbuf(Convert.FromBase64String(str)))
      | File(path) -> new Notification(summary, body, new Pixbuf(path))
    match data.Dismissible with
    | Some(func) -> [| { Label = "Dismiss"; Handler = fun _ -> func() } |]
    | None -> [||]
    |> Array.append data.Actions
    |> Array.iter (fun a ->
      Logger.trace <| sprintf "Notification: Adding action '%s'" a.Label
      notification.AddAction(a.Label, a.Label, fun _ args -> a.Handler args))
    Logger.trace <|
      sprintf "Notification: Summary = '%s' Body = '%s'"
        (notification.Summary.Trim())
        (notification.Body.Trim())
    notification.Show())
