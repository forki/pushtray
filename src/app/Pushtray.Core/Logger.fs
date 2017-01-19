module Pushtray.Logger

open System

type LogLevel =
  | Trace = 0
  | Info = 1
  | Debug = 2
  | Warn = 3
  | Error = 4
  | Fatal = 5

let private logLevelColors =
  [ LogLevel.Trace, ConsoleColor.Green
    LogLevel.Info,  ConsoleColor.Blue
    LogLevel.Debug, ConsoleColor.Cyan
    LogLevel.Warn,  ConsoleColor.Yellow
    LogLevel.Error, ConsoleColor.Red
    LogLevel.Fatal, ConsoleColor.DarkRed ]
  |> Map.ofList

let mutable minLogLevel =
  #if DEBUG
  LogLevel.Trace
  #else
  LogLevel.Warn
  #endif

let private writeWithColor color (str: string) =
  Console.ForegroundColor <- color
  Console.Write(str)
  Console.ResetColor()

let private write logLevel (str: string) =
  let printLogNameWithColor color =
    Enum.GetName(typeof<LogLevel>, logLevel).ToUpper()
    |> sprintf "%s "
    |> writeWithColor color
    |> ignore

  if logLevel >= minLogLevel then
    logLevelColors.TryFind logLevel |> Option.iter printLogNameWithColor
    System.Console.WriteLine(str)

let trace  str = write LogLevel.Trace  str
let info   str = write LogLevel.Info   str
let debug  str = write LogLevel.Debug  str
let warn   str = write LogLevel.Warn   str
let error  str = write LogLevel.Error  str
let fatal  str = write LogLevel.Fatal  str

let printInfo (str: string) =
  System.Console.WriteLine(str)
  write LogLevel.Info str
