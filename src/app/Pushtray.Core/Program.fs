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
        exit 0
    }

let private connect() =
  Pushbullet.Stream.connect args.Options
  Application.Init()

  if not args.Options.NoTrayIcon then
    TrayIcon.create args.Options.IconStyle

  // Ctrl-c doesn't seem to do anything after Application.Run() is called
  // so we'll handle SIGINT explicitly
  exitOnSignal Signum.SIGINT

  Application.Run()

let private sms() =
  Sms.send
    (Pushbullet.requestAccountData args.Options)
    args.Options.Device
    (required args.Positional.Number)
    (required args.Positional.Message)

let private list() =
  command "devices" <| fun () ->
    (Pushbullet.requestAccountData args.Options).Devices
    |> Array.iter (fun d ->
      printfn "%s (%s %s)" d.Nickname d.Manufacturer d.Model)

let private help() =
  printfn "%s" usageWithOptions

[<EntryPoint>]
let main argv =
  command "connect" connect
  command "sms" sms
  command "list" list
  commands [ "-h"; "--help" ] help
  0
