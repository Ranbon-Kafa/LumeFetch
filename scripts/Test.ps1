param([string]$Dotnet = 'dotnet', [switch]$IncludeMediaSmoke, [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    if (!$NoRestore) {
        & $Dotnet restore LumeFetch.sln
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    }
    & $Dotnet format LumeFetch.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Formatting check failed.' }
    & $Dotnet build LumeFetch.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $Dotnet test LumeFetch.sln -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($IncludeMediaSmoke) {
        & $Dotnet run --project tools/LumeFetch.Screenshot -c Release --no-build -- --smoke
        if ($LASTEXITCODE -ne 0) { throw 'Native/UI smoke failed.' }
    }
} finally { Pop-Location }
