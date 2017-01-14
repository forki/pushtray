open Pushnix.Pushbullet
open Pushnix.Cli

[<EntryPoint>]
let main argv =
  start (arg "<encrypt-pass>")
  0
