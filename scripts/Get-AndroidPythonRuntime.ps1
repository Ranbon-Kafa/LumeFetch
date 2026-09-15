param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $repo '.tools/android-native/downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$runtime = Get-Content (Join-Path $repo 'packaging/android/python-runtime.lock.json') -Raw | ConvertFrom-Json
foreach ($file in $runtime.files) {
    $path = Join-Path $cache $file.file
    if (!(Test-Path -LiteralPath $path)) {
        if ($Offline) { throw "Missing official Android Python: $($file.file)" }
        Invoke-WebRequest -Uri $file.url -OutFile $path -TimeoutSec 300
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Python runtime checksum mismatch: $($file.file)" }
    Write-Host "Verified official CPython: $($file.abi)"
}
