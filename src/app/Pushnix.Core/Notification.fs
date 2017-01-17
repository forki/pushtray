module Pushnix.Notification

open System
open Gdk
open Notifications
open Pushnix.Utils

type NotificationData =
  { Summary: NotificationText
    Body: NotificationText
    DeviceInfo: string option
    Timestamp: string option
    Icon: Icon
    Actions: Action[]
    Dismissible: bool }

and NotificationText =
  | Text of string
  | TextWithFormat of (string * (string -> string))

and Icon =
  | Base64 of string
  | File of string

and Action =
  { Label: string
    Handler: (ActionArgs -> unit) }

type Format =
  | Full
  | Short

let private format =
  Cli.arg "--notify-format"
  |> Option.map (fun s ->
    match s.ToLower() with
    | "full" -> Full
    | "short" -> Short
    | _ -> Short)
  |> Option.fold (fun _ v -> v) Short

let private lineWrapWidth = int <| defaultArg (Cli.arg "--notify-wrap") "40"
let private padWidth = int <| defaultArg (Cli.arg "--notify-padding") "42"
let private leftPad = "  "

let private wrap width line =
  Logger.trace <| sprintf "WRAP: %d" width
  let rec loop remaining result words =
    match words with
    | head :: tail ->
      // TODO: Fix HTML tags being included as words
      let (acc, remain) =
        if String.length head > remaining then (sprintf "%s\n" head, width)
        else (head + " ", remaining - head.Length)
      loop remain (result + acc) tail
    | _ -> result
  String.split [|' '|] line |> (List.ofArray >> loop width "")

let private pad width line =
  Logger.trace <| sprintf "Pad: Line = '%s' Length: %d" line line.Length
  (if String.length line < width then
    Logger.trace <| sprintf "Pad: %d - %d = %d" width line.Length (width - line.Length)
    line + (String.replicate (width - line.Length) " ")
  else
    Logger.trace <| sprintf "Pad: Line %d > Width %d" line.Length width
    line)
  |> sprintf "%s%s" leftPad

let private prettify str =
  // TODO: Improve this?
  // Doing a lot of splitting and concatting here so this is probably not optimal
  str
  |> String.split [|'\n'|]
  |> Array.collect (wrap lineWrapWidth >> String.split [|'\n'|])
  |> Array.map (pad padWidth)
  |> String.concat "\n"

let send data =
  let text = function
    | Text(str) -> prettify str
    | TextWithFormat(str, format) -> format <| prettify str
  let footer =
    match format with
    | Full ->
      sprintf "%s %s"
        (defaultArg data.DeviceInfo "")
        (defaultArg data.Timestamp "")
      |> prettify
      |> sprintf "\n<i>%s</i>"
    | Short -> ""
  let icon =
    match data.Icon with
    | Base64(str) -> new Pixbuf(Convert.FromBase64String(str))
    | File(path) -> new Pixbuf(path)

  Gtk.Application.Invoke(fun _ _ ->
    let notif =
      new Notification
        ( text data.Summary,
          text data.Body + footer,
          icon )
    [| { Label = "Dismiss"; Handler = fun _ -> notif.Close() } |]
    |> Array.append data.Actions
    |> Array.iter (fun a ->
    data.Actions |> Array.iter (fun a ->
      notif.AddAction(a.Label, a.Label, fun _ args -> a.Handler args))
    notif.AddAction("Dismiss", "Dissmis", fun _ _ -> notif.Close())
    notif.Show()))
