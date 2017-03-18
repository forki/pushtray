// Copyright (c) 2017 Jatan Patel
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

module Pushtray.Config

open System.IO
open System.Text.RegularExpressions

type Config =
  { AccessToken: string option
    EncryptPass: string option }

let private readConfigLine line =
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
    |> List.choose readConfigLine
    |> Map.ofList
  { AccessToken = values.TryFind "access_token"
    EncryptPass = values.TryFind "encrypt_pass" }

let config =
  Environment.userConfigFile |> Option.map readConfigFile
