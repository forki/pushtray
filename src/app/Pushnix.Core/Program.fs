open Gtk
open Pushnix

[<EntryPoint>]
let main argv =
  if Cli.argExists "--trace" then Logger.minLogLevel <- Logger.LogLevel.Trace

  Application.Init()
  Pushbullet.connect <| Cli.requiredArg "<encrypt-pass>"

  // TODO
  TrayIcon.create()
  Application.Run()

  0
