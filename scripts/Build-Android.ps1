param(
    [ValidateSet('android-arm64', 'android-x64')][string]$Runtime = 'android-arm64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?$')][string]$Version = '1.1.0-dev',
    [ValidateRange(1, 2100000000)][int]$VersionCode = 1,
    [string]$BundledToolsDirectory,
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA/Android/Sdk",
    [string]$JavaSdkDirectory = 'C:/Program Files/Android/Android Studio/jbr',
    [switch]$Offline
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
if (!$BundledToolsDirectory) { throw 'Supply -BundledToolsDirectory with a source-built private-SONAME runtime. See packaging/android/NATIVE-BUILD.md. The legacy AAR is not a release input.' }
$BundledToolsDirectory = (Resolve-Path -LiteralPath $BundledToolsDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $BundledToolsDirectory 'assets/tools/manifest.json'))) {
    throw 'Run scripts/Package-AndroidNativeTools.ps1 first.'
}
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $BundledToolsDirectory -Abi $(if ($Runtime -eq 'android-arm64') { 'arm64-v8a' } else { 'x86_64' })
# Android's Windows resource tools do not support all Unicode source paths.
# Build an isolated snapshot in an ASCII temporary directory; never move the checkout.
$stage = Join-Path ([IO.Path]::GetTempPath()) ('LumeFetch-Android-' + [Guid]::NewGuid().ToString('N'))
if ($stage -match '[^\x00-\x7F]') { throw 'Set TEMP to an ASCII path for Android builds.' }
New-Item -ItemType Directory -Path $stage | Out-Null
Write-Host "Isolated build directory: $stage"
foreach ($name in @('Directory.Build.props', 'Directory.Packages.props', 'global.json')) {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $stage
}
foreach ($name in @('LumeFetch.Android', 'LumeFetch.Presentation', 'LumeFetch.Core', 'LumeFetch.Infrastructure')) {
    $source = Join-Path $repo "src/$name"
    Get-ChildItem -LiteralPath $source -File -Recurse | Where-Object {
        [IO.Path]::GetRelativePath($source, $_.FullName) -notmatch '^(bin|obj)[\\/]'
    } | ForEach-Object {
        $target = Join-Path $stage "src/$name/$([IO.Path]::GetRelativePath($source, $_.FullName))"
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
}
$logoTarget = Join-Path $stage 'src/LumeFetch.Desktop/Assets'
New-Item -ItemType Directory -Force -Path $logoTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'src/LumeFetch.Desktop/Assets/lumefetch-logo.png') -Destination $logoTarget
# Junctions give native packaging tools ASCII paths to the existing verified caches.
$cacheRoot = Join-Path $stage '.tools'
New-Item -ItemType Directory -Path $cacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $repo '.tools/nuget/packages') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $cacheRoot 'android-backend') -Target (Join-Path $repo '.tools/android-backend') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $cacheRoot 'selected-android-tools') -Target $BundledToolsDirectory | Out-Null
New-Item -ItemType Junction -Path (Join-Path $cacheRoot 'nuget-packages') -Target (Join-Path $repo '.tools/nuget/packages') | Out-Null
$project = Join-Path $stage 'src/LumeFetch.Android/LumeFetch.Android.csproj'
$packages = Join-Path $cacheRoot 'nuget-packages'
$env:DOTNET_CLI_HOME = Join-Path $repo '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$restoreArgs = @('restore', $project, "-p:LumeFetchAndroidRuntime=$Runtime", '--packages', $packages, '-p:NuGetAudit=false')
if ($Offline) { $restoreArgs += @('--source', $packages) }
& $dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "Android restore failed; logs/intermediates retained at $stage" }
& $dotnet build $project -c $Configuration "-p:LumeFetchAndroidRuntime=$Runtime" --no-restore -m:1 `
    "-p:ApplicationDisplayVersion=$Version" "-p:ApplicationVersion=$VersionCode" `
    "-p:BundledToolsRoot=$(Join-Path $cacheRoot 'selected-android-tools')" `
    "-p:AndroidSdkDirectory=$AndroidSdkDirectory" "-p:JavaSdkDirectory=$JavaSdkDirectory" --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Android build failed; logs/intermediates retained at $stage" }
$apks = @(Get-ChildItem -LiteralPath (Join-Path $stage "src/LumeFetch.Android/bin/$Configuration/net10.0-android/$Runtime") -Filter '*-Signed.apk')
if ($apks.Count -ne 1) { throw 'Expected one locally signed development APK.' }
$output = Join-Path $repo "artifacts/android/$Runtime/$([IO.Path]::GetFileName($stage))"
New-Item -ItemType Directory -Force -Path $output | Out-Null
Copy-Item -LiteralPath $apks[0].FullName -Destination $output
Get-FileHash -LiteralPath (Join-Path $output $apks[0].Name) -Algorithm SHA256
Write-Host 'Development package only. Device acceptance and redistribution audit remain required.'
Write-Host "Build intermediates retained for diagnosis: $stage"
