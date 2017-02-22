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
  match args.Options.NotifyFormat with
  | "full" -> Full
  | "short" -> Short
  | str ->
    Logger.warn <| sprintf "Unknown notify-format value '%s'" str
    Short

let private wrapLine width line =
  let rec loop remaining result words =
    match words with
    | head :: tail ->
      let (acc, remain) =
        if String.length head > remaining then (sprintf "%s\n" head, width)
        else (head + " ", remaining - head.Length)
      loop remain (result + acc) tail
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
