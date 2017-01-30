module Pushtray.Cli

open System

let usage = "\
usage:
  pushtray connect [<encrypt-pass>] [options]
  pushtray send-sms <number> <message> [options]
  pushtray (-h | --help)"

let options = "\
options:
  --access-token=<token>      Set the access token. This will override the
                              config file value.
  --ignore-sms <numbers>      Don't show SMS notifications from these numbers
                              <numbers> is a comma-separated list or a single
                              asterisk to ignore all.
  --notify-format=<fmt>       Set notification format style (full | short)
  --notify-line-wrap=<wrap>   Set the line wrap width of notifications
                              (i.e. the maximum width)
  --notify-line-pad=<pad-to>  Set the minimum line width of notifications
  --icon-style=<style>        Customize the tray icon style (light | dark)
  --log=<log-level>           Enable all logging messages at <log-level>
                              and higher"

let usageWithOptions =
  sprintf "%s\n\n%s" usage options

let private parseArgs (argv: string[]) =
  let docopt = new DocoptNet.Docopt()
  docopt.PrintExit.Add(fun _ ->
    printfn "%s" usage
    exit 1)
  docopt.Apply(usageWithOptions, argv, help = false, exit = true)

let args = parseArgs <| System.Environment.GetCommandLineArgs().[1..]

let private valueOf func key =
  if args.ContainsKey(key) then
    match args.[key] with
    | null -> None
    | v -> Some <| func v
  else
    None

let argAsString key =
  key |> valueOf (fun v -> v.ToString())

let argAsSet key =
  match argAsString key with
  | Some s -> Set.ofArray <| s.Split [| ',' |]
  | None -> Set.empty

let argExists key =
  key |> valueOf (fun v -> v.IsTrue) |> Option.exists id

let requiredArg key =
  match argAsString key with
  | Some v -> v
  | None -> failwith <| sprintf "%s argument is required" key

let argWithDefault key defaultValue =
  defaultArg (argAsString key) defaultValue

let command key func =
  if argExists key then
    func()
    exit 0

let commands keys func =
  keys |> List.iter (fun k -> command k func)
