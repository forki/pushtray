open Mono.Unix
open Mono.Unix.Native
open Gtk
open Pushtray

let private exitOnSignal (signum: Signum) =
  Async.Start <|
    async {
      if (new UnixSignal(signum)).WaitOne() then
        Logger.info <| sprintf "Received %s, exiting..." (System.Enum.GetName(typeof<Signum>, signum))
        Application.Quit()
        exit 1
    }

[<EntryPoint>]
let main argv =
  Pushbullet.Stream.connect()

  Application.Init()
  TrayIcon.create()

  // Ctrl-c doesn't seem to do anything after Application.Run() is called
  // so we'll handle SIGINT explicitly
  exitOnSignal Signum.SIGINT

  Application.Run()
  0
