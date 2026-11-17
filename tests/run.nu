#!/usr/bin/env nu

let project_root = $env.FILE_PWD | path dirname
cd $project_root
if ($env.STARDEW_GAME_PATH? | default "" | is-empty) {
    error make {msg: "Set STARDEW_GAME_PATH to the directory containing the installed game DLLs. The game will not be launched."}
}

$env.DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env.DOTNET_NOLOGO = "1"
^nu --no-config-file build.nu
if $env.LAST_EXIT_CODE != 0 {
    exit $env.LAST_EXIT_CODE
}

$env.COOPTIME_ARTIFACT = $project_root | path join artifacts $"CoopTimeLedger-(open src/CoopTime.Mod/manifest.json | get Version).zip"
let started = date now
^dotnet test tests/CoopTime.Tests/CoopTime.Tests.csproj --configuration Release --logger "trx;LogFileName=tests.trx" --results-directory artifacts/test-results --collect "XPlat Code Coverage" --settings tests/coverage.runsettings
if $env.LAST_EXIT_CODE != 0 {
    exit $env.LAST_EXIT_CODE
}

let coverage = glob artifacts/test-results/*/coverage.cobertura.xml
    | each {|file| {file: $file, modified: (ls $file | first | get modified)} }
    | where modified >= $started | sort-by modified | last | get file
$env.COOPTIME_COVERAGE_PATH = $project_root | path join artifacts test-results coverage.cobertura.xml
$env.COOPTIME_REPORT_DIR = $project_root | path join artifacts test-results
let baseline_root = if ($project_root | path join artifacts baseline-122 tests.trx | path exists) {
    "baseline-122"
} else {
    "baseline-40"
}
let baseline = $project_root | path join artifacts $baseline_root tests.trx
if ($baseline | path exists) {
    $env.COOPTIME_BASELINE_TRX = $baseline
}

cp --force $coverage $env.COOPTIME_COVERAGE_PATH
^dotnet run --project tests/CoverageCheck/CoverageCheck.csproj --configuration Release
if $env.LAST_EXIT_CODE != 0 {
    exit $env.LAST_EXIT_CODE
}
