param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh source-package directory.' }
& (Join-Path $PSScriptRoot 'Get-AndroidNativeSources.ps1') -Offline
$archives = Join-Path $output 'archives'
$recipes = Join-Path $output 'lumefetch-build'
New-Item -ItemType Directory -Path $archives,$recipes -Force | Out-Null
foreach ($input in Get-Content (Join-Path $repo 'packaging/android/native-sources.lock.json') -Raw | ConvertFrom-Json) {
    Copy-Item -LiteralPath (Join-Path (Join-Path $repo $input.cache) $input.file) -Destination $archives
}
foreach ($package in Get-Content (Join-Path $repo 'packaging/android/python-packages.lock.json') -Raw | ConvertFrom-Json) {
    Copy-Item -LiteralPath (Join-Path $repo ".tools/android-native/downloads/$($package.sourceFile)") -Destination $archives
}
foreach ($file in @('packaging/android/native-sources.lock.json','packaging/android/python-packages.lock.json',
    'packaging/android/tools.lock.json','packaging/android/notices.lock.json','packaging/android/REDISTRIBUTION.md',
    'scripts/collect-android-notices.py','scripts/Get-AndroidExtractor.ps1',
    'packaging/android/python-runtime.lock.json','scripts/Get-AndroidPythonRuntime.ps1',
    'packaging/android/native/python-launcher.c','packaging/android/NATIVE-BUILD.md',
    'scripts/Build-AndroidNative.ps1','scripts/build-android-native.sh','scripts/android-clang.sh',
    'scripts/Get-AndroidNdk.ps1','scripts/Get-AndroidPythonPackages.ps1','scripts/Get-AndroidNativeSources.ps1',
    'scripts/Package-AndroidNativeTools.ps1','scripts/Test-AndroidLibraryIsolation.ps1',
    'src/LumeFetch.Android/Assets/tools/bootstrap.py')) {
    $path = Join-Path $recipes $file
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $path
}
Write-Host "Native source candidate: $output"
Write-Host 'This is not the full application source/notices package. Include .NET/Avalonia notices, actual build configuration and final APK acceptance evidence before publishing.'
