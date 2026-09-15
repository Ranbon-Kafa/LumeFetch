param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$inputs = Get-Content (Join-Path $repo 'packaging/android/native-sources.lock.json') -Raw | ConvertFrom-Json
foreach ($input in $inputs) {
    if ($input.cache -notin @('.tools/android-native/downloads','.tools/release-sources') -or $input.file -match '[/\\]' -or $input.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid pinned source entry.' }
    $directory = Join-Path $repo $input.cache
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $path = Join-Path $directory $input.file
    if (!(Test-Path -LiteralPath $path)) {
        if ($Offline) { throw "Missing source: $($input.file)" }
        Invoke-WebRequest -Uri $input.url -OutFile $path -TimeoutSec 300
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $input.sha256) { throw "Source checksum mismatch: $($input.file)" }
    Write-Host "Verified source: $($input.file)"
}
& (Join-Path $PSScriptRoot 'Get-AndroidPythonPackages.ps1') -Offline:$Offline
