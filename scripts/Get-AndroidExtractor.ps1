param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = Get-Content (Join-Path $repo 'packaging/android/tools.lock.json') -Raw | ConvertFrom-Json
# Only the extractor input is used, not the legacy Maven AARs in this lock.
$cache = Join-Path $repo ".tools/android-backend/$($lock.version)"
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$file = Join-Path $cache "yt-dlp-$($lock.extractor.version)"
if (!(Test-Path -LiteralPath $file)) {
    if ($Offline) { throw 'Pinned yt-dlp is not cached. Run without -Offline once at build time.' }
    Invoke-WebRequest -Uri $lock.extractor.url -OutFile $file
}
if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $lock.extractor.sha256) { throw 'Extractor checksum mismatch.' }
Write-Host "Verified official extractor $($lock.extractor.version). No Maven runtime downloaded."
