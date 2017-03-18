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

module Pushtray.Cli

open System
open Pushtray.Utils

let usage = "\
usage:
  pushtray connect [options]
  pushtray sms <number> <message> [--device=<name>] [options]
  pushtray list devices [options]
  pushtray (-h | --help)
  pushtray --version"

let options = "\
options:
  --access-token=<token>      Set the access token (overrides the config file
                              value).
  --encrypt-pass=<pass>       Set the encrypt password (overrides the config
                              file value).
  --no-tray-icon              Don't show a tray icon.
  --icon-style=<style>        Customize the tray icon style (light, dark)
  --enable-icon-animations    Show tray icon animations.
  --sms-notify-icon=<icon>    Change the stock icon for SMS notifications.
  --ignore-sms <numbers>      Don't show SMS notifications from these numbers.
                              <numbers> is a comma-separated list or a single
                              asterisk to ignore all.
  --notify-format=<fmt>       Set notification format style (full, short)
  --notify-line-wrap=<wrap>   Set the line wrap width of notifications
                              (i.e. the maximum width)
  --notify-line-pad=<pad-to>  Set the minimum line width of notifications
  --log=<log-level>           Enable all logging messages at <log-level>
                              and higher"

let version = sprintf "Pushtray %s" AssemblyInfo.Version

type Arguments =
  { Commands: Set<string>
    Positional: PositionalArgs
    Options: Options }

and PositionalArgs =
  { Number: string option
    Message: string option }

and Options =
  { Device: string option
    AccessToken: string option
    EncryptPass: string option
    NoTrayIcon: bool
    EnableIconAnimations: bool
    SmsNotifyIcon: string option
    IgnoreSms: Set<string>
    NotifyFormat: string
    NotifyLineWrap: int
    NotifyLinePad: int
    IconStyle: string
    Log: string }

let usageWithOptions =
  sprintf "%s\n\n%s" usage options

type DocoptArgs = Collections.Generic.IDictionary<string, DocoptNet.ValueObject>

let private parseArgs (argv: string[]) =
  let docopt = new DocoptNet.Docopt()
  docopt.PrintExit.Add(fun _ ->
    printfn "%s" usage
    exit 1)
  docopt.Apply(usageWithOptions, argv, help = false, exit = true)

let args =
  let docoptArgs: DocoptArgs option =
    #if INTERACTIVE
    None
    #else
    Some (parseArgs <| System.Environment.GetCommandLineArgs().[1..])
    #endif

  let valueOf func key =
    docoptArgs |> Option.bind (fun a ->
      if a.ContainsKey(key) then
        match a.[key] with
        | null -> None
        | v -> Some <| func v
      else
        None)

  let argAsString key = key |> valueOf (fun v -> v.ToString())
  let argAsBool key = key |> valueOf (fun v -> v.IsTrue) |> Option.exists id
  let argAsSet key =
    match argAsString key with
    | Some s -> Set.ofArray <| s.Split [| ',' |]
    | None -> Set.empty

  { Commands =
      Set [ "connect"
            "sms"
            "list"
            "devices"
            "-h"; "--help"
            "--version" ]
      |> Set.filter argAsBool
    Positional =
      { Number  = argAsString "<number>"
        Message = argAsString "<message>" }
    Options =
      { Device                = argAsString "--device"
        AccessToken           = argAsString "--access-token"
        EncryptPass           = argAsString "--encrypt-pass"
        NoTrayIcon            = argAsBool   "--no-tray-icon"
        EnableIconAnimations  = argAsBool   "--enable-icon-animations"
        SmsNotifyIcon         = argAsString "--sms-notify-icon"
        IgnoreSms             = argAsSet    "--ignore-sms"
        NotifyFormat          = argAsString "--notify-format"    |> Option.getOrElse "short"
        NotifyLineWrap        = argAsString "--notify-line-wrap" |> Option.getOrElse "40" |> int
        NotifyLinePad         = argAsString "--notify-line-pad"  |> Option.getOrElse "45" |> int
        IconStyle             = argAsString "--icon-style"       |> Option.getOrElse "light"
        Log                   = argAsString "--log"              |> Option.getOrElse "warn" } }

let required opt =
  match opt with
  | Some v -> v
  | None ->
    Logger.fatal "Required argument has no value"
    exit 1

let command key func =
  if args.Commands.Contains key then
    func()
    exit 0

let commands keys func =
  for k in keys do command k func
