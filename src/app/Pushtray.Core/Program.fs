open Mono.Unix
open Mono.Unix.Native
open Gtk
open Pushtray

[<EntryPoint>]
let main argv =
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
