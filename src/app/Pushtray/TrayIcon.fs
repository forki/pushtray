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

module Pushtray.TrayIcon

open System.IO
open System.Threading
open Gtk
open Pushtray.Cli
open Pushtray.Utils

type IconStyle =
  | Light
  | Dark

let private iconStyleFromString (str: string) =
  match str.ToLower() with
  | "light" -> Light
  | "dark" -> Dark
  | str ->
    Logger.warn <| sprintf "Unknown --icon-style value '%s'" str
    Light

type IconState =
  | Connected
  | Sync0
  | Sync1
  | Sync2
  | Sync3

let private iconFileNameSuffix = function
  | Connected -> ""
  | Sync0 -> "-sync0"
  | Sync1 -> "-sync1"
  | Sync2 -> "-sync2"
  | Sync3 -> "-sync3"

type IconData =
  | File of Gdk.Pixbuf
  | Stock of string

let private getAppDataAbsolutePath relativePath =
  Environment.appDataDirs
  |> Option.bind (fun paths ->
    paths
    |> List.map (fun p -> Path.Combine [| p; relativePath |])
    |> List.tryFind File.Exists)

let private iconMap (iconStyle: string) =
  let basePath =
    match iconStyleFromString iconStyle with
    | Light -> "pushbullet-tray-light"
    | Dark -> "pushbullet-tray-dark"
    |> sprintf "icons/%s"
  [ Connected
    Sync0
    Sync1
    Sync2
    Sync3 ]
  |> List.map (fun state ->
    let data =
      let absolutePath =
        iconFileNameSuffix state
        |> sprintf "%s%s.svg" basePath
        |> getAppDataAbsolutePath
      match absolutePath with
      | Some path -> File(new Gdk.Pixbuf(path))
      | None -> Stock("phone")
    (state, data))
  |> Map.ofList

let iconDataForState state (map: Map<IconState, IconData>) =
  map
  |> Map.tryFind state
  |> Option.getOrElse (Stock("phone"))

type TrayIcon(iconStyle: string) =
  let icons = iconMap iconStyle

  let aboutDialog =
    let about = new AboutDialog()
    about.ProgramName <- AssemblyInfo.Product
    about.Version <- AssemblyInfo.Version
    about.LogoIconName <- Stock.Home
    about.Copyright <- sprintf "(C) %s" AssemblyInfo.Author
    about.Website <- AssemblyInfo.Website
    about.WebsiteLabel <- AssemblyInfo.Website
    about

  let show (dialog: Gtk.Dialog) =
    let window = new Window("Dummy")
    window.Visible <- false
    dialog.TransientFor <- window
    dialog.Run() |> ignore
    dialog.Hide()
    window.Dispose()

  let onTrayIconPopup args =
    let menuItem (text: string) (image: string) onActivated =
      let item = new ImageMenuItem(text)
      item.Image <- new Gtk.Image(image, IconSize.Menu)
      item.Activated.Add(onActivated)
      item

    let popupMenu = new Menu()
    popupMenu.Add(menuItem "About" Stock.About (fun _ -> show aboutDialog))
    popupMenu.Add(menuItem "Quit" Stock.Quit (fun _ -> exit 0))
    popupMenu.ShowAll()
    popupMenu.Popup()

  let icon =
    let trayIcon =
      match iconDataForState IconState.Sync0 icons with
      | File(p) -> new StatusIcon(p)
      | Stock(name) -> StatusIcon.NewFromIconName(name)
    trayIcon.TooltipText <- "Pushtray"
    trayIcon.Visible <- true
    trayIcon.PopupMenu.Add(onTrayIconPopup)
    trayIcon

  let update state =
    Gtk.Application.Invoke (fun _ _ ->
      match iconDataForState state icons with
      | File(p) -> icon.Pixbuf <- p
      | Stock(name) -> icon.IconName <- name)

  let mutable cancelSyncing: CancellationTokenSource option = None

  member this.ShowSyncing() =
    let cancel = new CancellationTokenSource()
    Async.Start <|
      async {
        if args.Options.EnableIconAnimations then
          for _ in 0 .. 3 do
            if not <| cancel.IsCancellationRequested then
              for state in [| Sync0; Sync1; Sync2; Sync3 |] do
                update state
                Thread.Sleep(250)
        else
          update Sync0
      }
    cancelSyncing |> Option.iter (fun c -> c.Cancel())
    lock this (fun _ -> cancelSyncing <- Some cancel)

  member this.ShowConnected() =
    Async.Start <|
      async {
        cancelSyncing |> Option.iter (fun c -> c.Cancel())
        do! Async.Sleep(1000)
        update Connected
      }
