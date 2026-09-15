param([string]$Output = 'artifacts/android/runtime-inventory.json')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$prepared = Join-Path $repo '.tools/android-backend/prepared'
$manifestPath = Join-Path $prepared 'assets/tools/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$inventory = [Collections.Generic.List[object]]::new()
foreach ($file in $manifest.files) {
    $relative = if ($file.kind -eq 'native') { "jni/$($file.abi)/$($file.name)" } else { "assets/$($file.asset)" }
    $path = Join-Path $prepared $relative
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "Unverified input: $relative" }
    if ($file.kind -ne 'archive') { continue }
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) { continue }
            $symlink = (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000
            $stream = $entry.Open()
            try {
                $head = [byte[]]::new(4)
                $read = $stream.Read($head, 0, 4)
                $elf = $read -eq 4 -and $head[0] -eq 0x7F -and $head[1] -eq 0x45 -and $head[2] -eq 0x4C -and $head[3] -eq 0x46
            } finally { $stream.Dispose() }
            $notice = $entry.FullName -match '(?i)(license|copying|copyright|\.dist-info/(METADATA|RECORD))'
            if (!$elf -and !$symlink -and !$notice) { continue }
            $stream = $entry.Open()
            try {
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
            } finally { $stream.Dispose() }
            $target = $null
            if ($symlink) {
                $reader = [IO.StreamReader]::new($entry.Open())
                try { $target = $reader.ReadToEnd() } finally { $reader.Dispose() }
            }
            $inventory.Add([ordered]@{
                abi = $file.abi; payload = $file.name; path = $entry.FullName; bytes = $entry.Length
                kind = $(if ($symlink) { 'symlink' } elseif ($elf) { 'elf' } else { 'notice-or-metadata' })
                sha256 = $hash; target = $target
                correspondingSourceVerified = $false
            })
        }
    } finally { $archive.Dispose() }
}
$report = [ordered]@{
    schemaVersion = 1; publicDistributionApproved = $false
    manifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    packages = $manifest.packages; extractor = $manifest.extractor
    launchers = @($manifest.files | Where-Object kind -eq 'native')
    inventory = $inventory
    warning = 'Binary inventory only, not a source/license attestation. See packaging/android/README.md.'
}
$destination = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$root = [IO.Path]::GetFullPath($repo).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$destination.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory output must stay in the repository.' }
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
[IO.File]::WriteAllText($destination, ($report | ConvertTo-Json -Depth 10))
$inventory | Group-Object abi,kind | Select-Object Count,Name
Write-Host "Inventory: $destination"
Write-Host 'Public distribution remains blocked; no files were published.'
