param([Parameter(Mandatory)][string]$Destination, [string]$Version = '1.0.0', [Parameter(Mandatory)][string]$FFmpegDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repo '.tools/source-bundle'
$inventory = Get-Content -LiteralPath (Join-Path $sourceRoot 'source-inventory.json') -Raw | ConvertFrom-Json
$review = Get-Content -LiteralPath (Join-Path $sourceRoot 'review-gaps.json') -Raw | ConvertFrom-Json
if ($review.failed.Count -or $review.noLicenseFileInArchive.Count) { throw 'Unresolved source/notice inventory. Run collect-third-party.py and review its report.' }
$staging = Join-Path $Destination 'corresponding-sources'
if (Test-Path -LiteralPath $staging) { throw 'Use a new source staging directory.' }
New-Item -ItemType Directory -Path (Join-Path $staging 'archives') -Force | Out-Null
foreach ($item in $inventory) {
    if ($item.file -notmatch '^archives/[^/\\]+$') { throw 'Invalid source inventory path.' }
    $source = Join-Path $sourceRoot $item.file
    if ((Get-FileHash -LiteralPath $source).Hash -ne $item.sha256) { throw "Source archive changed: $($item.file)" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging $item.file)
}
foreach ($file in @('source-inventory.json','review-gaps.json')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $staging
}
Copy-Item -LiteralPath (Join-Path $repo '.tools/licenses') -Destination (Join-Path $staging 'licenses') -Recurse
New-Item -ItemType Directory -Path (Join-Path $staging 'scripts'), (Join-Path $staging 'packaging/windows') -Force | Out-Null
foreach ($file in @('Build-FFmpeg.ps1','build-ffmpeg.sh','collect-third-party.py','Get-WindowsTools.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $staging 'scripts')
}
foreach ($file in @('sources.json','dependencies.json')) {
    Copy-Item -LiteralPath (Join-Path $repo "packaging/windows/$file") -Destination (Join-Path $staging 'packaging/windows')
}
Copy-Item -LiteralPath (Join-Path $repo 'docs/CORRESPONDING-SOURCES.md') -Destination (Join-Path $staging 'README.md')
Copy-Item -LiteralPath (Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $staging
& (Join-Path $FFmpegDirectory 'ffmpeg.exe') -hide_banner -buildconf 2>&1 | Set-Content -LiteralPath (Join-Path $staging 'ffmpeg-buildconf.txt') -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw 'Cannot record FFmpeg build configuration.' }
$zip = Join-Path $Destination "LumeFetch-$Version-corresponding-sources.zip"
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Sources: $zip"
