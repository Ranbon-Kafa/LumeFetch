param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $repo '.tools/android-native/downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$packages = Get-Content (Join-Path $repo 'packaging/android/python-packages.lock.json') -Raw | ConvertFrom-Json
foreach ($package in $packages) {
    foreach ($input in @(
        @{File=$package.file; Url=$package.url; Hash=$package.sha256},
        @{File=$package.sourceFile; Url=$package.sourceUrl; Hash=$package.sourceSha256}
    )) {
        $path = Join-Path $cache $input.File
        if (!(Test-Path -LiteralPath $path)) {
            if ($Offline) { throw "Missing pinned Python package: $($input.File)" }
            Invoke-WebRequest -Uri $input.Url -OutFile $path -TimeoutSec 180
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $input.Hash) {
            throw "Python package checksum mismatch: $($input.File)"
        }
        Write-Host "Verified $($input.File)"
    }
}
