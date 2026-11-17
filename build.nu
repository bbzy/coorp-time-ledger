#!/usr/bin/env nu

let project_root = $env.FILE_PWD
cd $project_root
if ($env.STARDEW_GAME_PATH? | default "" | is-empty) {
    error make {msg: "Set STARDEW_GAME_PATH to the installed game DLL directory. This script never launches or installs the game."}
}

$env.DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env.DOTNET_NOLOGO = "1"
^dotnet build src/CoopTime.Mod/CoopTime.Mod.csproj --configuration Release
if $env.LAST_EXIT_CODE != 0 {
    exit $env.LAST_EXIT_CODE
}

let version = open src/CoopTime.Mod/manifest.json | get Version
let package = $project_root | path join artifacts CoopTimeLedger
let archive = $project_root | path join artifacts $"CoopTimeLedger-($version).zip"
if ($package | path exists) {
    rm -r $package
}

if ($archive | path exists) {
    rm $archive
}

mkdir $package
let output = $project_root | path join src CoopTime.Mod bin Release net6.0
for name in [CoopTime.dll CoopTime.Core.dll manifest.json] {
    cp ($output | path join $name) $package
}

cd ($project_root | path join artifacts)
^zip -q -r $archive CoopTimeLedger
if $env.LAST_EXIT_CODE != 0 {
    exit $env.LAST_EXIT_CODE
}

print $archive
