module Pushtray.TrayIcon

open Gtk;
open Gdk;

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
  let trayIcon = StatusIcon.NewFromIconName("pushbullet-light")
  trayIcon.TooltipText <- "Pushbullet"
  trayIcon.Visible <- true
  trayIcon.PopupMenu.Add(onTrayIconPopup)