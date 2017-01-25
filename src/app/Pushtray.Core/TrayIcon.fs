module Pushtray.TrayIcon

open System.IO
open Gtk
open Pushtray.Utils

type IconStyle =
  | Light
  | Dark

let private iconStyle =
  match Cli.argWithDefault "--icon-style" "light" with
  | "light" -> Light
  | "dark" -> Dark
  | str -> Logger.warn <| sprintf "Unknown --icon-style value '%s'" str; Light

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
    appDataDirs
    |> Option.bind (fun paths ->
      paths
      |> List.map (fun p -> Path.Combine [| p; iconPath |])
      |> List.tryFind File.Exists)
    |> function
    | Some path -> new StatusIcon(new Gdk.Pixbuf(path))
    | None ->
      Logger.warn "Tray icon not found, falling back to generic icon"
      StatusIcon.NewFromIconName("phone")

  trayIcon.TooltipText <- "Pushtray"
  trayIcon.Visible <- true
  trayIcon.PopupMenu.Add(onTrayIconPopup)
