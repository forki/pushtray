module Pushtray.TrayIcon

open System.IO
open Gtk
open Gdk

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
  let trayIcon =
    Utils.appDataDir
    |> Option.map (fun p -> Path.Combine [| p; "icons/scalable/pushbullet-light.svg" |])
    |> Option.filter File.Exists
    |> function
    | Some path -> new StatusIcon(new Pixbuf(path))
    | None ->
      Logger.warn "Pushbullet icon was not found, falling back to stock icon"
      StatusIcon.NewFromIconName("phone")
  trayIcon.TooltipText <- "Pushbullet"
  trayIcon.Visible <- true
  trayIcon.PopupMenu.Add(onTrayIconPopup)
