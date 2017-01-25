module Pushtray.Notification

open System
open Gdk
open Notifications
open Pushtray.Utils

type NotificationData =
  { Summary: NotificationText
    Body: NotificationText
    DeviceInfo: string option
    Timestamp: string option
    Icon: Icon
    Actions: Action[]
    Dismissible: (unit -> Async<string option>) option }

and NotificationText =
  | Text of string
  | TextWithFormat of (string * (string -> string))

and Icon =
  | Stock of string
  | Base64 of string
  | File of string

and Action =
  { Label: string
    Handler: (ActionArgs -> unit) }

type Format =
  | Full
  | Short

let private format =
  match Cli.argWithDefault "--notify-format" "short" with
  | "full" -> Full
  | "short" -> Short
  | str -> Logger.warn <| sprintf "Invalid notification format '%s'" str; Short

let private lineWrapWidth = int <| Cli.argWithDefault "--notify-line-wrap" "40"
let private linePadWidth = int <| Cli.argWithDefault "--notify-line-pad" "45"
let private leftPad = "  "

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
  |> sprintf "%s%s" leftPad

let private prettify text =
  text
  |> String.split [|'\n'|]
  |> Array.collect (wrapLine lineWrapWidth >> String.split [|'\n'|])
  |> Array.map (padLine linePadWidth)
  |> String.concat "\n"

let private dismiss asyncRequest (notification: Notification) (args: ActionArgs) =
  asyncRequest()
  |> Async.Ignore
  |> Async.Start
  notification.Close()

let send data =
  let footer =
    match format with
    | Full ->
      sprintf "%s %s"
        (defaultArg data.DeviceInfo "")
        (defaultArg data.Timestamp "")
      |> prettify
      |> sprintf "\n<i>%s</i>"
    | Short -> ""

  let formatText = function
    | Text(str) -> prettify str
    | TextWithFormat(str, format) -> format <| prettify str
  let summary = formatText data.Summary
  let body = formatText data.Body + footer

  Gtk.Application.Invoke(fun _ _ ->
    let notification =
      match data.Icon with
      | Stock(str) -> new Notification(summary, body, str)
      | Base64(str) -> new Notification(summary, body, new Pixbuf(Convert.FromBase64String(str)))
      | File(path) -> new Notification(summary, body, new Pixbuf(path))

    match data.Dismissible with
    | Some(request) -> [| { Label = "Dismiss"; Handler = (dismiss request notification) } |]
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
