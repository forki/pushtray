open Mono.Unix
open Mono.Unix.Native
open Gtk
open Pushtray

open System.Threading

[<EntryPoint>]
let main argv =
  if Cli.argExists "--trace" then Logger.minLogLevel <- Logger.LogLevel.Trace

  Application.Init()
  Pushbullet.connect <| Cli.requiredArg "<encrypt-pass>"

  // Exit gracefully after receiving SIGINT
  Async.Start(async {
    if (new UnixSignal(Signum.SIGINT)).WaitOne() then
      Logger.info "Received SIGINT, exiting..."
      Application.Quit()
      exit 1
  })

  TrayIcon.create()
  Application.Run()

  0
