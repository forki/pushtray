#r "packages/FAKE/tools/FakeLib.dll"

open Fake
open Fake.FileUtils

let projectName = "Pushtray"
let solution = sprintf "%s.sln" projectName
let binary = sprintf "%s.exe" <| projectName.ToLower()
let version = "0.1.2";
let config = getBuildParamOrDefault "config" "Release"
let platformTarget = getBuildParamOrDefault "platformTarget" "AnyCPU"

Target "Build" (fun () ->
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

let buildDir = "build"
let buildDirWithConfig = buildDir </> "output" </> config
let buildMergeDir = buildDirWithConfig </> "merge"
let buildMergeBinary = buildMergeDir </> binary

Target "Repack" (fun () ->
  let toolPath = "packages" </> "ILRepack" </> "tools" </> "ILRepack.exe"
  let inputBinary = buildDirWithConfig </> binary

  let libs =
    [ "FSharp.Core"
      "FSharp.Data"
      "notify-sharp"
      "websocket-sharp"
      "BouncyCastle.Crypto"
      "DocoptNet" ]
    |> List.map (fun v -> buildDirWithConfig </> (v + ".dll"))
    |> String.concat " "

  mkdir buildMergeDir
  sprintf "%s /xmldocs /out:%s %s %s"
    toolPath
    buildMergeBinary
    inputBinary
    libs
  |> execMono
  |> ignore)

let distDir = buildDir </> "dist"

Target "Release" (fun () ->
  mkdir distDir
  cp buildMergeBinary distDir)

Target "Clean" (fun () ->
  CleanDir buildDir)

"Clean"
  ==> "Build"
  ==> "Repack"
  ==> "Release"

RunTargetOrDefault "Build"
