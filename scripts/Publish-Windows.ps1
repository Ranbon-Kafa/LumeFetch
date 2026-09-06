param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0',
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler,
    [string]$FFmpegDirectory,
    [switch]$SkipInstaller,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$repo = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $repo '.tools'
if (!$FFmpegDirectory) { $FFmpegDirectory = Join-Path $tools 'ffmpeg-lumefetch' }
$FFmpegDirectory = [IO.Path]::GetFullPath($FFmpegDirectory)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifacts = Join-Path $repo "artifacts/releases/$Version-$stamp"
$publish = Join-Path $artifacts 'app'
New-Item -ItemType Directory -Path $publish -Force | Out-Null
foreach ($tool in @('yt-dlp/yt-dlp.exe','deno/deno.exe')) {
    if (!(Test-Path -LiteralPath (Join-Path $tools $tool))) { throw "Missing $tool. Run scripts/Get-WindowsTools.ps1 first." }
}
foreach ($file in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $FFmpegDirectory $file))) { throw "Missing FFmpeg file: $file" }
}
[string[]]$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
& $Dotnet publish (Join-Path $repo 'src/LumeFetch.Desktop/LumeFetch.Desktop.csproj') -c Release -r win-x64 --self-contained true -o $publish "-p:Version=$Version" "-p:DebugType=None" "-p:DebugSymbols=false" "-p:PublishTrimmed=false" @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
# Native-package debug symbols are unnecessary in an end-user bundle.
# Only delete generated PDB files inside this exact fresh publish directory.
$publishRoot = [System.IO.Path]::GetFullPath($publish).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($symbol in Get-ChildItem -LiteralPath $publish -File -Filter '*.pdb') {
    $symbolPath = [System.IO.Path]::GetFullPath($symbol.FullName)
    if (!$symbolPath.StartsWith($publishRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Symbol path escaped publish directory.' }
    Remove-Item -LiteralPath $symbolPath
}
foreach ($tool in @('yt-dlp', 'deno', 'ffmpeg')) {
    $target = Join-Path $publish "tools/$tool"
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    if ($tool -eq 'ffmpeg') {
        Copy-Item -Path (Join-Path $FFmpegDirectory '*.dll') -Destination $target
        foreach ($file in @('ffmpeg.exe','ffprobe.exe','LICENSE.txt')) { Copy-Item -LiteralPath (Join-Path $FFmpegDirectory $file) -Destination $target }
    } else { Copy-Item -LiteralPath (Join-Path $tools "$tool/$tool.exe") -Destination $target }
}
Copy-Item -LiteralPath (Join-Path $tools 'licenses') -Destination (Join-Path $publish 'licenses') -Recurse
foreach ($file in @('LICENSE', 'README.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $publish
}
Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows/dependencies.json') -Destination $publish
Copy-Item -LiteralPath (Join-Path $repo 'packaging/windows/sources.json') -Destination (Join-Path $publish 'licenses/pinned-sources.json')
foreach ($file in @('source-inventory.json', 'review-gaps.json')) {
    Copy-Item -LiteralPath (Join-Path $tools "source-bundle/$file") -Destination (Join-Path $publish "licenses/$file")
}
$binaryInventory = Get-ChildItem -LiteralPath (Join-Path $publish 'tools') -Recurse -File | ForEach-Object {
    [pscustomobject]@{ File = [IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/'); SHA256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
}
$binaryInventory | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'licenses/tool-binaries.json') -Encoding utf8
& (Join-Path $FFmpegDirectory 'ffmpeg.exe') -hide_banner -buildconf 2>&1 | Set-Content -LiteralPath (Join-Path $publish 'licenses/ffmpeg-buildconf.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $repo 'docs/RELEASE-CHECKLIST.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination (Join-Path $publish 'docs') -Recurse
# Include exact NuGet dependency inventory and any license/notice files shipped by packages.
$assets = Get-Content (Join-Path $repo 'src/LumeFetch.Desktop/obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($file in @('LICENSE.txt', 'THIRD-PARTY-NOTICES.TXT')) {
    $runtimeFile = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $runtimeFile = Get-ChildItem -Path (Join-Path $folder "microsoft.netcore.app.runtime.win-x64/*/$file") -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($runtimeFile) { break }
    }
    if (!$runtimeFile) { throw "Required .NET runtime notice missing: $file" }
    Copy-Item -LiteralPath $runtimeFile.FullName -Destination (Join-Path $publish "licenses/dotnet-$file")
}
$inventory = foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    $packagePath = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $library.Value.path
        if (Test-Path -LiteralPath $candidate) { $packagePath = $candidate; break }
    }
    if (!$packagePath) { throw "Cannot inventory package: $($library.Name)" }
    $spec = Get-ChildItem -LiteralPath $packagePath -Filter '*.nuspec' | Select-Object -First 1
    if (!$spec) { continue }
    [xml]$xml = Get-Content -LiteralPath $spec.FullName -Raw
    [pscustomobject]@{ Package = $library.Name; License = $xml.package.metadata.license.InnerText; Copyright = $xml.package.metadata.copyright; Project = $xml.package.metadata.projectUrl }
    $noticeFiles = Get-ChildItem -LiteralPath $packagePath -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|THIRD.PARTY.NOTICES|OFL)(\..*)?$' }
    foreach ($notice in $noticeFiles) {
        $safeName = $library.Name.Replace('/', '-') + '-' + $notice.Name
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $publish "licenses/$safeName") -Force
    }
}
$inventory | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'licenses/nuget-dependencies.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Package-Sources.ps1') -Destination $artifacts -Version $Version -FFmpegDirectory $FFmpegDirectory
if (!$SkipInstaller) {
    if (!$InnoCompiler) { $InnoCompiler = Join-Path $tools 'inno/ISCC.exe' }
    if (!(Test-Path -LiteralPath $InnoCompiler)) { throw 'Inno Setup compiler not found. Pass -InnoCompiler or run Get-WindowsTools.ps1 -IncludeInstallerCompiler.' }
    & $InnoCompiler '/Qp' "/DAppVersion=$Version" "/DPublishDir=$publish" "/DArtifactsDir=$artifacts" (Join-Path $repo 'packaging/windows/LumeFetch.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
New-Item -ItemType File -Path (Join-Path $publish 'portable.flag') | Out-Null
$zip = Join-Path $artifacts "LumeFetch-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hashes = Get-ChildItem -LiteralPath $artifacts -File | Where-Object { $_.Extension -in @('.zip', '.exe') } | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
}
$hashes | Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding utf8
Write-Host "Artifacts: $artifacts"
