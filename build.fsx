#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.FileUtils

let projectName = "Pushnix"
let solution = sprintf "%s.sln" projectName
let binary = sprintf "%s.exe" <| projectName.ToLower()
let version = "0.1.0";
let config = getBuildParamOrDefault "config" "Release"
let platformTarget = getBuildParamOrDefault "platformTarget" "AnyCPU"
let outputDir = "output"
let outputDirWithConfig = outputDir </> config

Target "Build" (fun _ ->
  let setParams defaults =
    { defaults with
        Targets = ["Build"]
        Verbosity = Some MSBuildVerbosity.Minimal
        Properties =
          [ "Configuration",   config
            "PlatformTarget",  platformTarget ]
    }
  build setParams solution)

let execMono cmd =
  directExec(fun info ->
    info.FileName <- "mono"
    info.Arguments <- cmd)

Target "Repack" (fun _ ->
  let toolPath = "packages" </> "ILRepack" </> "tools" </> "ILRepack.exe"
  let inputBinary = outputDirWithConfig </> binary
  let outputMergeDir = outputDirWithConfig </> "merge"
  let outputBinary = outputMergeDir </> binary

  let libs =
    [ "FSharp.Core"
      "FSharp.Data"
      "websocket-sharp"
      "BouncyCastle.Crypto"
      "DocoptNet" ]
    |> List.map (fun v -> outputDirWithConfig </> (v + ".dll"))
    |> String.concat " "

  mkdir outputMergeDir
  sprintf "%s /xmldocs /out:%s %s %s"
    toolPath
    outputBinary
    inputBinary
    libs
  |> execMono
  |> ignore)

Target "Clean" (fun _ ->
  CleanDir outputDir)

// TODO
Target "Release" ignore

"Build"
  ==> "Repack"
  ==> "Release"

RunTargetOrDefault "Build"
