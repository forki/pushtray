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

module Pushtray.Environment

open System
open System.IO

let appDataDirs =
  try
    let dataDirs =
      [ Environment.SpecialFolder.ApplicationData
        Environment.SpecialFolder.CommonApplicationData ]
      |> List.map (fun p -> Path.Combine(Environment.GetFolderPath(p), "pushtray"))
    Some <| (AppDomain.CurrentDomain.BaseDirectory :: dataDirs)
  with ex ->
    Logger.debug <| sprintf "DataDir: %s" ex.Message
    None

let userConfigDir =
  Path.Combine
    [| Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
       "pushtray" |]

let userConfigFile =
  let filePath = Path.Combine(userConfigDir, "config")
  if File.Exists(filePath) then Some filePath else None
