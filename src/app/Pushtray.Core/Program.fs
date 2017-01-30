open Mono.Unix
open Mono.Unix.Native
open Gtk
open Pushtray
open Pushtray.Cli

let private exitOnSignal (signum: Signum) =
  Async.Start <|
    async {
      if (new UnixSignal(signum)).WaitOne() then
        Logger.info <| sprintf "Received %s, exiting..." (System.Enum.GetName(typeof<Signum>, signum))
        Application.Quit()
        exit 1
    }

let private connect() =
  Pushbullet.Stream.connect()
  Application.Init()
  TrayIcon.create <| Cli.argWithDefault "--icon-style" "light"

  // Ctrl-c doesn't seem to do anything after Application.Run() is called
  // so we'll handle SIGINT explicitly
  exitOnSignal Signum.SIGINT

  Application.Run()

let private sendSms() =
  Sms.send (requiredArg "<number>") (requiredArg "<message>")

let private printHelp() =
  printfn "%s" usageWithOptions

[<EntryPoint>]
let main argv =
  command "connect" connect
  command "send-sms" sendSms
  commands [ "-h"; "--help" ] printHelp
  0
