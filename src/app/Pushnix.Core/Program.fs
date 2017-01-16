open Gtk;
open Pushnix

[<EntryPoint>]
let main argv =
  Application.Init()
  Pushbullet.connect <| Cli.requiredArg "<encrypt-pass>"

  // TODO
  TrayIcon.create()
  Application.Run()

  0
