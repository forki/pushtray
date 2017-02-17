module Pushtray.Config

open System.IO
open System.Text.RegularExpressions

type Config =
  { AccessToken: string option
    EncryptPass: string option }

let private parseConfigLine line =
  if Regex.Match(line, "^\s*#.*$").Success then
    None
  else
    let m = Regex.Match(line, "^\s*([\S]+)\s?=\s?([\S]+)$")
    if m.Success then
      match [ for g in m.Groups -> g.Value ] with
      | _ :: key :: value :: _ -> Some (key, value)
      | _ -> None
    else
      None

let readConfigFile (filePath: string) =
  use reader = new StreamReader(filePath)
  let values =
    [ while not reader.EndOfStream do yield reader.ReadLine() ]
    |> List.choose parseConfigLine
    |> Map.ofList
  { AccessToken = values.TryFind "access_token"
    EncryptPass = values.TryFind "encrypt_pass" }

let config =
  Environment.userConfigFile |> Option.map readConfigFile
