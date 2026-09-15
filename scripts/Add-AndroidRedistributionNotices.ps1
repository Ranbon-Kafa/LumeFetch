param(
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [Parameter(Mandatory)][ValidateSet('arm64-v8a','x86_64')][string]$Abi,
    [Parameter(Mandatory)][string]$NoticesDirectory,
    [Parameter(Mandatory)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$runtime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
$notices = (Resolve-Path -LiteralPath $NoticesDirectory).Path
$output = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh output directory.' }
if (!(Test-Path -LiteralPath (Join-Path $notices 'inventory.json'))) { throw 'A collected notice inventory is required.' }
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $runtime -Abi $Abi
$manifest = Get-Content -LiteralPath (Join-Path $runtime 'assets/tools/manifest.json') -Raw | ConvertFrom-Json
foreach ($file in $manifest.files) {
    if ($file.abi -notin @($Abi, 'any')) { throw 'Expected a single-ABI runtime.' }
    $relative = if ($file.kind -eq 'native') { "jni/$Abi/$($file.name)" } else { "assets/$($file.asset)" }
    if ($relative -match '(?:^|[\\/])\.\.(?:[\\/]|$)|:') { throw 'Unsafe manifest path.' }
    $source = Join-Path $runtime $relative
    if ((Get-FileHash -LiteralPath $source).Hash -ine $file.sha256) { throw "Runtime hash mismatch: $relative" }
    $target = Join-Path $output $relative
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
$noticeOutput = Join-Path $output 'assets/notices'
New-Item -ItemType Directory -Path $noticeOutput -Force | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($notices, (Join-Path $noticeOutput 'third-party-notices.zip'))
Copy-Item -LiteralPath (Join-Path $notices 'INDEX.md') -Destination $noticeOutput
$manifest.PSObject.Properties.Remove('distributionApproved')
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'assets/tools/manifest.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $output -Abi $Abi
Write-Host 'All native/runtime bytes retained and checksum-verified; only notice assets and manifest metadata changed.'
