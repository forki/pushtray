module Pushtray.TrayIcon

open System.IO
open Gtk
open Gdk

type IconStyle =
  | Light
  | Dark

let private iconStyle =
  match Cli.argWithDefault "--icon-style" "light" with
  | "light" -> Light
  | "dark" -> Dark
  | str -> Logger.warn <| sprintf "Unknown icon style '%s'" str; Light

let private onTrayIconPopup args =
  let menuItemQuit = new ImageMenuItem("Quit")
  let quitImage = new Gtk.Image(Stock.Quit, IconSize.Menu)
  menuItemQuit.Image <- quitImage
  menuItemQuit.Activated.Add(fun _ -> Application.Quit())

  let popupMenu = new Menu()
  popupMenu.Add(menuItemQuit)
  popupMenu.ShowAll()
  popupMenu.Popup()

let create() =
  let iconPath =
    match iconStyle with
    | Light -> "pushbullet-tray-light.svg"
    | Dark -> "pushbullet-tray-dark.svg"
    |> sprintf "icons/%s"

  let trayIcon =
    Utils.appDataDir
    |> Option.map (fun p -> Path.Combine [| p; iconPath |])
    |> Option.filter File.Exists
    |> function
    | Some path -> new StatusIcon(new Pixbuf(path))
    | None ->
      Logger.warn "Pushbullet icon was not found, falling back to generic icon"
      StatusIcon.NewFromIconName("phone")

  trayIcon.TooltipText <- "Pushbullet"
  trayIcon.Visible <- true
  trayIcon.PopupMenu.Add(onTrayIconPopup)
