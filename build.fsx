#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.FileUtils

let projectName = "Pushtray"
let solution = sprintf "%s.sln" projectName
let binary = sprintf "%s.exe" <| projectName.ToLower()
let version = "0.1.0";
let config = getBuildParamOrDefault "config" "Release"
let platformTarget = getBuildParamOrDefault "platformTarget" "AnyCPU"

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

let outputDir = "output"
let outputDirWithConfig = outputDir </> config
let outputMergeDir = outputDirWithConfig </> "merge"
let outputMergeBinary = outputMergeDir </> binary

Target "Repack" (fun _ ->
  let toolPath = "packages" </> "ILRepack" </> "tools" </> "ILRepack.exe"
  let inputBinary = outputDirWithConfig </> binary

  let libs =
    [ "FSharp.Core"
      "FSharp.Data"
      "notify-sharp"
      "websocket-sharp"
      "BouncyCastle.Crypto"
      "DocoptNet" ]
    |> List.map (fun v -> outputDirWithConfig </> (v + ".dll"))
    |> String.concat " "

  mkdir outputMergeDir
  sprintf "%s /xmldocs /out:%s %s %s"
    toolPath
    outputMergeBinary
    inputBinary
    libs
  |> execMono
  |> ignore)

let releaseDir = "dist"
let dataDir = "data"

Target "Release" (fun _ ->
  mkdir releaseDir
  cp outputMergeBinary releaseDir

  let releaseIconsDir = releaseDir </> "icons"
  mkdir releaseIconsDir
  cp_r (dataDir </> "icons") releaseIconsDir)

Target "Clean" (fun _ ->
  CleanDir outputDir
  CleanDir releaseDir)

"Clean"
  ==> "Build"
  ==> "Repack"
  ==> "Release"

RunTargetOrDefault "Build"
