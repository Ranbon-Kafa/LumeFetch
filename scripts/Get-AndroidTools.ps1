param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$lock = Get-Content (Join-Path $repo 'packaging/android/tools.lock.json') -Raw | ConvertFrom-Json
$cache = Join-Path $repo ".tools/android-backend/$($lock.version)"
$prepared = Join-Path $repo '.tools/android-backend/prepared'
New-Item -ItemType Directory -Path $cache, $prepared -Force | Out-Null
$manifest = [ordered]@{ version = $lock.version; packages = $lock.packages; extractor = $lock.extractor; files = [Collections.Generic.List[object]]::new() }

function Export-Entry($archive, [string]$entryName, [string]$relative, [string]$abi, [string]$kind, [string]$runtimeName) {
    $entry = $archive.GetEntry($entryName)
    if (!$entry) { throw "Missing packaged tool: $entryName" }
    $path = [IO.Path]::GetFullPath((Join-Path $prepared $relative))
    $root = [IO.Path]::GetFullPath($prepared).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe output path.' }
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
    $manifest.files.Add([ordered]@{
        abi = $abi; kind = $kind; name = $runtimeName
        asset = $(if ($kind -ne 'native') { $relative.Substring('assets/'.Length).Replace('\', '/') } else { $null })
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes = (Get-Item -LiteralPath $path).Length
    })
}

foreach ($package in $lock.packages) {
    $file = Join-Path $cache "$($package.name)-$($lock.version).aar"
    if (!(Test-Path -LiteralPath $file)) {
        if ($Offline) { throw "Download required: $($package.url)" }
        Invoke-WebRequest -Uri $package.url -OutFile $file -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $package.sha256) { throw "Checksum mismatch: $file" }
    $archive = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        foreach ($abi in @('arm64-v8a', 'x86_64')) {
            if ($package.name -eq 'library') {
                foreach ($name in @('libpython.so', 'libqjs.so')) {
                    Export-Entry $archive "jni/$abi/$name" "jni/$abi/$name" $abi 'native' $name
                }
                Export-Entry $archive "jni/$abi/libpython.zip.so" "assets/tools/$abi/python.zip" $abi 'archive' 'python'
            } else {
                foreach ($name in @('libffmpeg.so', 'libffprobe.so')) {
                    Export-Entry $archive "jni/$abi/$name" "jni/$abi/$name" $abi 'native' $name
                }
                Export-Entry $archive "jni/$abi/libffmpeg.zip.so" "assets/tools/$abi/ffmpeg.zip" $abi 'archive' 'ffmpeg'
            }
        }
    } finally { $archive.Dispose() }
}
$extractor = Join-Path $cache "yt-dlp-$($lock.extractor.version)"
if (!(Test-Path -LiteralPath $extractor)) {
    if ($Offline) { throw "Download required: $($lock.extractor.url)" }
    Invoke-WebRequest -Uri $lock.extractor.url -OutFile $extractor -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $extractor -Algorithm SHA256).Hash -ne $lock.extractor.sha256) { throw 'yt-dlp checksum mismatch.' }
Copy-Item -LiteralPath $extractor -Destination (Join-Path $prepared 'assets/tools/yt-dlp')
$manifest.files.Add([ordered]@{
    abi = 'any'; kind = 'file'; name = 'yt-dlp'; asset = 'tools/yt-dlp'
    sha256 = $lock.extractor.sha256; bytes = (Get-Item -LiteralPath $extractor).Length
})
[IO.File]::WriteAllText((Join-Path $prepared 'assets/tools/manifest.json'), ($manifest | ConvertTo-Json -Depth 8))
Write-Host 'Prepared verified Android native tools (no Java wrapper library included).'
Write-Host 'Distribution remains gated by native acceptance and corresponding-source/license verification.'
