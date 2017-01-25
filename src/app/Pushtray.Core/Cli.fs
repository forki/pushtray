module Pushtray.Cli

open System

let usage = "\
usage:
  pushtray <access-token> [<encrypt-pass>] [options]

options:
  --ignore-sms <numbers>      Don't show SMS notifications from these phone numbers
                              (given as a comma-separated list or a single asterisk to ignore all)
  --notify-format=<fmt>       Set notification format style (full, short)
  --notify-line-wrap=<wrap>   Set the line wrap width of notifications
                              (i.e. the maximum width)
  --notify-line-pad=<pad-to>  Set the minimum line width of notifications
  --icon-style=<style>        Customize the tray icon style (light, dark)
  --log=<log-level>           Enable all logging messages at <log-level> and higher"

let private parseArgs (argv: string[]) =
  let docopt = new DocoptNet.Docopt()
  docopt.PrintExit.Add(fun _ ->
    printfn "%s" <| usage
    exit 1)
  docopt.Apply(usage, argv, help = false, exit = true)

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
  | None -> failwith (sprintf "%s argument is required" key)

let argWithDefault key defaultValue =
  defaultArg (argAsString key) defaultValue
