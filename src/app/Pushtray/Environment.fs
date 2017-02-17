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
