param(
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][ValidateSet('arm64-v8a', 'x86_64')][string]$Abi,
    [Parameter(Mandatory)][string]$Destination,
    [string]$CollectedNoticesDirectory
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = if ($Abi -eq 'arm64-v8a') { 'aarch64-linux-android' } else { 'x86_64-linux-android' }
$work = (Resolve-Path -LiteralPath $WorkDirectory).Path
$prefix = Join-Path $work "$target/prefix"
$python = Join-Path $work "python-$target/prefix"
$output = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh candidate output directory. Existing runtime inputs are never overwritten.' }
foreach ($file in @('ffmpeg','ffprobe','qjs','python-launcher')) {
    if (!(Test-Path -LiteralPath (Join-Path $prefix "bin/$file"))) { throw "Missing native build: $file" }
}
& (Join-Path $PSScriptRoot 'Get-AndroidPythonPackages.ps1') -Offline
& (Join-Path $PSScriptRoot 'Get-AndroidExtractor.ps1') -Offline
$extractorLock = (Get-Content (Join-Path $repo 'packaging/android/tools.lock.json') -Raw | ConvertFrom-Json)
$extractor = Join-Path $repo ".tools/android-backend/$($extractorLock.version)/yt-dlp-$($extractorLock.extractor.version)"
if ((Get-FileHash -LiteralPath $extractor -Algorithm SHA256).Hash -ne $extractorLock.extractor.sha256) { throw 'Extractor checksum mismatch.' }
$staging = Join-Path ([IO.Path]::GetTempPath()) ('LumeFetch-NativePackage-' + [Guid]::NewGuid().ToString('N'))
$manifest = [ordered]@{
    version = 'cpython3.14.7-ffmpeg9.0.1-qjs2026.06.04-candidate2'
    libraryPolicy = 'python-private-sonames-v1'
    runtime = 'official-cpython-and-lumefetch-source-builds'
    extractor = $extractorLock.extractor
    files = [Collections.Generic.List[object]]::new()
}
New-Item -ItemType Directory -Path (Join-Path $output "jni/$Abi"), (Join-Path $output "assets/tools/$Abi"), (Join-Path $output 'assets/notices') -Force | Out-Null
function Add-ManifestFile([string]$path, [string]$kind, [string]$name, [string]$asset, [string]$architecture) {
    $manifest.files.Add([ordered]@{abi=$architecture; kind=$kind; name=$name; asset=$asset
        sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        bytes=(Get-Item -LiteralPath $path).Length})
}
function New-RuntimeZip([string]$root, [string]$path) {
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected runtime symlink: $($file.FullName)" }
            $name = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.ExternalAttributes = (0x81A4 -shl 16) # regular file, 0644
            $entry.LastWriteTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $source = [IO.File]::OpenRead($file.FullName)
            $destinationStream = $entry.Open()
            try { $source.CopyTo($destinationStream) }
            finally { $destinationStream.Dispose(); $source.Dispose() }
        }
    } finally { $zip.Dispose() }
}
$names = @{ffmpeg='libffmpeg.so'; ffprobe='libffprobe.so'; qjs='libqjs.so'; 'python-launcher'='libpython.so'}
foreach ($name in $names.Keys) {
    $path = Join-Path $output "jni/$Abi/$($names[$name])"
    Copy-Item -LiteralPath (Join-Path $prefix "bin/$name") -Destination $path
    Add-ManifestFile $path 'native' $names[$name] $null $Abi
}
# Preserve the existing private runtime layout; no desktop or installer-owned path changes.
$pythonLib = Join-Path $staging 'python/usr/lib'
$ffmpegLib = Join-Path $staging 'ffmpeg/usr/lib'
New-Item -ItemType Directory -Path $pythonLib,$ffmpegLib -Force | Out-Null
# The SDK also contains linker aliases named libcrypto.so/libssl.so/libsqlite3.so.
# They belong in the build prefix, NOT on Android's runtime LD_LIBRARY_PATH: a
# Samsung system dependency then loads our OpenSSL in place of its own BoringSSL.
# Keep only CPython's private SONAMEs (as required by Python's Android guide).
foreach ($name in @('libpython3.14.so', 'libpython3.so', 'libcrypto_python.so', 'libssl_python.so', 'libsqlite3_python.so')) {
    Copy-Item -LiteralPath (Join-Path $python "lib/$name") -Destination $pythonLib
}
Copy-Item -LiteralPath (Join-Path $python 'lib/python3.14') -Destination $pythonLib -Recurse
Get-ChildItem -LiteralPath (Join-Path $prefix 'lib') -File -Filter '*.so*' | Copy-Item -Destination $ffmpegLib
$site = Join-Path $pythonLib 'python3.14/site-packages'
New-Item -ItemType Directory -Path $site -Force | Out-Null
foreach ($package in Get-Content (Join-Path $repo 'packaging/android/python-packages.lock.json') -Raw | ConvertFrom-Json) {
    $wheel = Join-Path $repo ".tools/android-native/downloads/$($package.file)"
    [IO.Compression.ZipFile]::ExtractToDirectory($wheel, $site)
}
$certDirectory = Join-Path $staging 'python/usr/etc/tls'
New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $site 'certifi/cacert.pem') -Destination (Join-Path $certDirectory 'cert.pem')
foreach ($name in @('python','ffmpeg')) {
    $path = Join-Path $output "assets/tools/$Abi/$name.zip"
    New-RuntimeZip (Join-Path $staging $name) $path
    Add-ManifestFile $path 'archive' $name "tools/$Abi/$name.zip" $Abi
}
$extractorPath = Join-Path $output 'assets/tools/yt-dlp'
Copy-Item -LiteralPath $extractor -Destination $extractorPath
Add-ManifestFile $extractorPath 'file' 'yt-dlp' 'tools/yt-dlp' 'any'
$notices = Join-Path $output 'assets/notices'
$noticeInputs = @{
    'CPython-LICENSE.txt' = (Join-Path $python 'lib/python3.14/LICENSE.txt')
    'QuickJS-LICENSE.txt' = (Join-Path $work 'host/quickjs-2026-06-04/LICENSE')
    'FFmpeg-LGPL-3.0.txt' = (Join-Path $work "$target/build/ffmpeg-9.0.1/COPYING.LGPLv3")
    'FFmpeg-GPL-3.0-reference.txt' = (Join-Path $work "$target/build/ffmpeg-9.0.1/COPYING.GPLv3")
    'LAME-COPYING.txt' = (Join-Path $work "$target/build/lame-3.100/COPYING")
    'Opus-COPYING.txt' = (Join-Path $work "$target/build/opus-1.6.1/COPYING")
    'zlib-README.txt' = (Join-Path $work "$target/build/zlib-1.3.2/README")
}
foreach ($name in $noticeInputs.Keys) { Copy-Item -LiteralPath $noticeInputs[$name] -Destination (Join-Path $notices $name) }
Copy-Item -LiteralPath (Join-Path $repo 'packaging/android/python-packages.lock.json') -Destination $notices
if ($CollectedNoticesDirectory) {
    $collected = (Resolve-Path -LiteralPath $CollectedNoticesDirectory).Path
    if (!(Test-Path -LiteralPath (Join-Path $collected 'inventory.json'))) { throw 'Run collect-android-notices.py first.' }
    # Keep original upstream paths inside a ZIP: Windows aapt2 has path-length limits.
    [IO.Compression.ZipFile]::CreateFromDirectory($collected, (Join-Path $notices 'third-party-notices.zip'))
    Copy-Item -LiteralPath (Join-Path $collected 'INDEX.md') -Destination (Join-Path $notices 'INDEX.md')
}
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $output -Abi $Abi
# Manifest is the final readiness marker. Failure above leaves an unusable candidate,
# never a partially overwritten production runtime.
[IO.File]::WriteAllText((Join-Path $output 'assets/tools/manifest.json'), ($manifest | ConvertTo-Json -Depth 8))
Write-Host "Native candidate: $output"
Write-Host "Staging retained: $staging"
Write-Host 'NOT release-approved. Verify ELF alignment, native tests, all transitive notices/sources and physical-device acceptance.'
