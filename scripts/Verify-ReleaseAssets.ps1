param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{40}$')][string]$Commit,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0'
)
$ErrorActionPreference = 'Stop'
$directoryPath = [IO.Path]::GetFullPath($Directory)
if ((Get-Content -LiteralPath (Join-Path $directoryPath 'COMMIT.txt') -Raw).Trim() -ne $Commit) { throw 'Artifact commit differs from tested commit.' }
$expected = @("LumeFetch-$Version-win-x64-setup.exe", "LumeFetch-$Version-win-x64-portable.zip", "LumeFetch-$Version-corresponding-sources.zip")
$hashes = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $directoryPath 'SHA256SUMS.txt')) {
    if ($line -notmatch '^([a-f0-9]{64})  ([^/\\]+)$') { throw 'Invalid checksum line.' }
    if ($hashes.ContainsKey($Matches[2])) { throw 'Duplicate checksum entry.' }
    $hashes[$Matches[2]] = $Matches[1]
}
if ($hashes.Count -ne $expected.Count) { throw 'Unexpected checksum inventory.' }
foreach ($name in $expected) {
    if (!$hashes.ContainsKey($name)) { throw "Missing checksum: $name" }
    if ((Get-FileHash -LiteralPath (Join-Path $directoryPath $name)).Hash -ne $hashes[$name]) { throw "Checksum mismatch: $name" }
    Write-Host "PASS SHA256: $name"
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-ZipText($Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    if (!$entry) { throw "Missing archive entry: $Name" }
    if ($entry.Length -gt 4000000) { throw "Text entry too large: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Assert-ZipPaths($Zip) {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Zip.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name -match '(^/|^[A-Za-z]:|(^|/)\.\.(/|$))' -or !$names.Add($name)) { throw "Unsafe/duplicate archive path: $name" }
    }
}
$portable = [IO.Compression.ZipFile]::OpenRead((Join-Path $directoryPath $expected[1]))
try {
    Assert-ZipPaths $portable
    foreach ($name in @('LumeFetch.exe','LumeFetch.dll','portable.flag','tools/yt-dlp/yt-dlp.exe','tools/deno/deno.exe',
        'tools/ffmpeg/ffmpeg.exe','tools/ffmpeg/ffprobe.exe','tools/ffmpeg/libwinpthread-1.dll',
        'licenses/dotnet-LICENSE.txt','licenses/dotnet-THIRD-PARTY-NOTICES.TXT','licenses/Inter-LICENSE.txt')) {
        if (!$portable.GetEntry($name)) { throw "Required bundled file missing: $name" }
    }
    if ($portable.Entries | Where-Object { $_.FullName -match '(^|/)(settings\.json|\.env|cookies[^/]*\.txt)$|\.(pfx|p12|pdb)$' }) { throw 'Unexpected user/config/debug file in portable.' }
    $runtime = Read-ZipText $portable 'LumeFetch.runtimeconfig.json' | ConvertFrom-Json
    if (!$runtime.runtimeOptions.includedFrameworks) { throw 'Portable is not self-contained.' }
    $binaries = Read-ZipText $portable 'licenses/tool-binaries.json' | ConvertFrom-Json
    foreach ($item in $binaries) {
        $entry = $portable.GetEntry($item.File)
        if (!$entry) { throw "Missing inventoried binary: $($item.File)" }
        $stream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($actual -ne $item.SHA256) { throw "Bundled tool hash mismatch: $($item.File)" }
    }
    $configuration = Read-ZipText $portable 'licenses/ffmpeg-buildconf.txt'
    if ($configuration -match '--enable-(gpl|nonfree)' -or $configuration -notmatch '--enable-libmp3lame') { throw 'Unexpected FFmpeg configuration.' }
    foreach ($entry in $portable.Entries | Where-Object { $_.FullName -match '\.(md|json|txt)$' -and $_.Length -lt 4000000 }) {
        if ((Read-ZipText $portable $entry.FullName) -match '(?i)(C:[/\\]Users[/\\]RFRED|/c/Users/RFRED)') { throw "Private build path in $($entry.FullName)" }
    }
    Write-Host 'PASS: self-contained portable, bundled tool hashes, runtime/font notices and no local user data.'
} finally { $portable.Dispose() }
$sources = [IO.Compression.ZipFile]::OpenRead((Join-Path $directoryPath $expected[2]))
try {
    Assert-ZipPaths $sources
    $review = Read-ZipText $sources 'review-gaps.json' | ConvertFrom-Json
    if ($review.failed.Count -or $review.noLicenseFileInArchive.Count) { throw 'Unresolved source inventory.' }
    $inventory = Read-ZipText $sources 'source-inventory.json' | ConvertFrom-Json
    foreach ($item in $inventory) {
        $entry = $sources.GetEntry($item.file)
        if (!$entry) { throw "Missing corresponding source: $($item.file)" }
        $stream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($actual -ne $item.sha256) { throw "Corresponding source hash mismatch: $($item.file)" }
    }
    foreach ($name in @('scripts/Build-FFmpeg.ps1','scripts/build-ffmpeg.sh','packaging/windows/sources.json','ffmpeg-buildconf.txt')) {
        if (!$sources.GetEntry($name)) { throw "Missing build recipe: $name" }
    }
    Write-Host "PASS: $($inventory.Count) source archives/inputs and build recipes; $($review.upstreamDeclarationOnly.Count) explicitly recorded declaration-only notices."
} finally { $sources.Dispose() }
Write-Host "Verified release assets for commit $Commit. No executable was run."
