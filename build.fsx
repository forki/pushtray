#r "packages/FAKE/tools/FakeLib.dll"

open Fake

let projectName = "Pushnix"
let projectFile = sprintf "Pushnix.sln"
let version = "0.1.0";
let config = getBuildParamOrDefault "config" "Debug"
let platformTarget = getBuildParamOrDefault "platformTarget" "AnyCPU"

let buildDir = "build"
let buildDirWithConfig = combinePaths buildDir config

Target "Build" (fun _ ->
  let setParams defaults =
    { defaults with
        Targets = ["Build"]
        Verbosity = Some MSBuildVerbosity.Minimal
        Properties =
          [ "Configuration",   config
            "PlatformTarget",  platformTarget ]
    }
  build setParams projectFile)

RunTargetOrDefault "Build"
