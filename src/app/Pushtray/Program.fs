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

open Gtk
open Pushtray
open Pushtray.Stream
open Pushtray.TrayIcon
open Pushtray.Cli

let private connect() =
  Application.Init()
  let trayIcon =
    if not args.Options.NoTrayIcon then Some <| TrayIcon(args.Options.IconStyle)
    else None
  Async.Start <|
    async {
      let update =
        trayIcon |> Option.map (fun t ->
          { OnConnected = t.ShowConnected
            OnDisconnected = t.ShowSyncing })
      connect update args.Options
    }
  Application.Run()

let private sms() =
  Sms.send
    (Pushbullet.requestAccountData args.Options)
    args.Options.Device
    (required args.Positional.Number)
    (required args.Positional.Message)

let private list() =
  command "devices" <| fun () ->
    (Pushbullet.requestAccountData args.Options).Devices
    |> Array.iter (fun d ->
      printfn "%s (%s %s)" d.Nickname d.Manufacturer d.Model)

let private help() =
  printfn "%s" usageWithOptions

let private version() =
  printfn "%s" version

[<EntryPoint>]
let main argv =
  Logger.setMinLogLevel Cli.args.Options.Log
  command "connect" connect
  command "sms" sms
  command "list" list
  commands [ "-h"; "--help" ] help
  command "--version" version
  0
