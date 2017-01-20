module Pushtray.Cli

open System

let usage = "\
usage:
  pushtray <access-token> <encrypt-pass> [options]

options:
  --notify-format <fmt>       Select the notification format style (full, short)
  --notify-line-wrap <wrap>   Set the line wrap width (i.e. the maximum width)
  --notify-line-pad <pad>     Set the minimum line width
  --icon-style <style>        Select tray icon style (light, dark)
  --trace                     Print all log messages"

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

let arg key =
  key |> valueOf (fun v -> v.ToString())

let argExists key =
  key |> valueOf (fun v -> v.IsTrue) |> Option.exists id

let requiredArg key =
  match arg key with
  | Some v -> v
  | None -> failwith (sprintf "%s argument is required" key)

let argWithDefault key defaultValue =
  defaultArg (arg key) defaultValue
