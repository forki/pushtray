module Pushnix.Notification

open System
open Gdk
open Notifications
open Pushnix.Utils

type Format =
  | Full
  | Short

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

let private format =
  Cli.arg "--format"
  |> Option.map (fun s ->
    match s.ToLower() with
    | "full" -> Full
    | "short" -> Short
    | _ -> Short)
  |> Option.fold (fun _ v -> v) Short

let private leftPad = "  "

let private wrap width line =
  let rec loop remaining result words =
    match words with
    | head :: tail ->
      // TODO: Fix HTML tags being included as words
      let (acc, remain) =
        if String.length head > remaining then (sprintf "%s\n%s" head leftPad, width)
        else (head + " ", remaining - head.Length)
      loop remain (result + acc) tail
    | _ -> result
  String.split [|' '|] line |> (List.ofArray >> loop width "")

let private pad width line =
  (if String.length line < width then
    line + (String.replicate (width - String.length line) " ")
  else
    line)
  |> sprintf "%s%s" leftPad

let private prettify str =
  str
  |> String.split [|'\n'|]
  |> Array.map (wrap 40 >> pad 42)
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
    |> Array.iter (fun a -> notif.AddAction(a.Label, a.Label, fun _ args -> a.Handler args))
    notif.Show())
