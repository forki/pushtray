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

let mutable private minLogLevel =
  #if DEBUG
  LogLevel.Warn
  #else
  LogLevel.Trace
  #endif

let setMinLogLevel (str: string) =
  minLogLevel <-
    match str.ToLower() with
    | "trace" -> LogLevel.Trace
    | "info" -> LogLevel.Info
    | "debug" -> LogLevel.Debug
    | "warn" -> LogLevel.Warn
    | "error" -> LogLevel.Error
    | _ -> LogLevel.Warn

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
