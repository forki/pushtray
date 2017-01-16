module Pushnix.Cli

open System

let usage = """
usage:
  pushbullet <access-token> <encrypt-pass> [--show-device]
"""

let private parseArgs (argv: string[]) =
  let docopt = new DocoptNet.Docopt()
  docopt.PrintExit.Add(fun _ ->
    printfn "%s" <| usage
    System.Environment.Exit(1))
  docopt.Apply(usage, argv, help = false, exit = true)

let args = parseArgs <| System.Environment.GetCommandLineArgs().[1..]

let arg key  =
  if args.ContainsKey(key) then
    match args.[key] with
    | null -> None
    | v -> Some <| v.ToString()
  else
    None

let argExists key =
  match args.[key] with
  | null -> false
  | v -> v.IsTrue

let requiredArg key =
  match arg key with
  | Some v -> v
  | None -> failwith (sprintf "%s argument is required" key)

let argWithDefault defaultValue key =
  defaultArg (arg key) defaultValue
