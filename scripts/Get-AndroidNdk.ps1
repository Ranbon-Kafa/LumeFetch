param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $repo '.tools/android-native/downloads'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$zip = Join-Path $cache 'android-ndk-r30-iwr.zip'
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw 'Choose a fresh NDK extraction directory.' }
if (!(Test-Path -LiteralPath $zip)) {
    Invoke-WebRequest -Uri 'https://dl.google.com/android/repository/android-ndk-r30-windows.zip' `
        -Headers @{'Accept-Encoding'='identity'} -OutFile $zip -TimeoutSec 900
}
# SHA-1 and size are published by developer.android.com/ndk/downloads.
# SHA-256 additionally pins our verified complete download. CDN content encoding
# can make range lengths differ, so never extract based on Content-Length alone.
if ((Get-Item -LiteralPath $zip).Length -ne 727980788 -or
    (Get-FileHash -LiteralPath $zip -Algorithm SHA1).Hash -ne '9bf167a1985fa7d4a036186b78f702eab9179408' -or
    (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne 'b830098aaf18b67a42eb831c404e15e5f2990a474f054ac145b0bc957ac6d729') {
    throw 'Official NDK checksum mismatch. Nothing extracted or executed. Invalid archive retained for inspection.'
}
[IO.Compression.ZipFile]::ExtractToDirectory($zip, $destinationPath)
Write-Host "Verified NDK: $destinationPath/android-ndk-r30"
