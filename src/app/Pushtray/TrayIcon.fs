module Pushtray.TrayIcon

open System.IO
open System.Threading
open Gtk
open Pushtray.Cli
open Pushtray.Utils

type IconStyle =
  | Light
  | Dark

let private iconStyleFromString str =
  match str with
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

let private iconFileNameSuffix state =
  match state with
  | Connected -> ""
  | Sync0 -> "-sync0"
  | Sync1 -> "-sync1"
  | Sync2 -> "-sync2"
  | Sync3 -> "-sync3"

type IconData =
  | File of Gdk.Pixbuf
  | Stock of string

let private getAppDataAbsolutePath relativePath =
  appDataDirs
  |> Option.bind (fun paths ->
    paths
    |> List.map (fun p -> Path.Combine [| p; relativePath |])
    |> List.tryFind File.Exists)

let private iconMap iconStyle =
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

  let onTrayIconPopup args =
    let menuItemQuit = new ImageMenuItem("Quit")
    let quitImage = new Gtk.Image(Stock.Quit, IconSize.Menu)
    menuItemQuit.Image <- quitImage
    menuItemQuit.Activated.Add(fun _ -> exit 0)

    let popupMenu = new Menu()
    popupMenu.Add(menuItemQuit)
    popupMenu.ShowAll()
    popupMenu.Popup()

  let icon =
    let trayIcon =
      match iconDataForState IconState.Connected icons with
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
        if not args.Options.NoIconAnimations then
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
