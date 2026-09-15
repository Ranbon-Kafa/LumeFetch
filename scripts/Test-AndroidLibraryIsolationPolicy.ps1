# Small synthetic name-policy fixtures. Native ELF/signature/device tests are separate.
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('LumeFetch-LibraryPolicy-' + [Guid]::NewGuid().ToString('N'))
$required = @('libpython3.14.so', 'libcrypto_python.so', 'libssl_python.so', 'libsqlite3_python.so')
function New-Fixture([string]$name, [string[]]$pythonNames, [string[]]$ffmpegNames, [string[]]$nativeNames) {
    $candidate = Join-Path $root $name
    $native = Join-Path $candidate 'jni/arm64-v8a'
    $assets = Join-Path $candidate 'assets/tools/arm64-v8a'
    New-Item -ItemType Directory -Path $native,$assets -Force | Out-Null
    foreach ($file in $nativeNames) { New-Item -ItemType File -Path (Join-Path $native $file) | Out-Null }
    foreach ($kind in @('python','ffmpeg')) {
        $names = if ($kind -eq 'python') { $pythonNames } else { $ffmpegNames }
        $zip = [IO.Compression.ZipFile]::Open((Join-Path $assets "$kind.zip"), [IO.Compression.ZipArchiveMode]::Create)
        try { foreach ($file in $names) { [void]$zip.CreateEntry("usr/lib/$file") } } finally { $zip.Dispose() }
    }
    return $candidate
}
function Assert-Rejected([string]$candidate, [string]$pattern) {
    try { & (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $candidate -Abi arm64-v8a }
    catch { if ($_.Exception.Message -like $pattern) { return }; throw }
    throw "Unsafe fixture unexpectedly accepted: $candidate"
}
$good = New-Fixture 'valid' $required @('libavcodec.so') @('libffmpeg.so')
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $good -Abi arm64-v8a
foreach ($alias in @('libcrypto.so', 'libssl.so', 'libsqlite.so', 'libsqlite3.so', 'libsqlite3.so.0')) {
    $candidate = New-Fixture "python-$alias" ($required + $alias) @('libavcodec.so') @('libffmpeg.so')
    Assert-Rejected $candidate 'System-library shadowing alias:*'
}
Assert-Rejected (New-Fixture 'ffmpeg-alias' $required @('libcrypto.so') @('libffmpeg.so')) 'System-library shadowing alias:*'
Assert-Rejected (New-Fixture 'native-alias' $required @('libavcodec.so') @('libssl.so')) 'System-library shadowing alias:*'
Assert-Rejected (New-Fixture 'missing-private' @('libpython3.14.so') @('libavcodec.so') @('libffmpeg.so')) 'Missing private Python library:*'
Assert-Rejected (New-Fixture 'unknown-private' ($required + 'libunknown.so') @('libavcodec.so') @('libffmpeg.so')) 'Unexpected Python runtime library:*'
Assert-Rejected (New-Fixture 'duplicate-python' ($required + 'libpython3.15.so') @('libavcodec.so') @('libffmpeg.so')) 'Expected exactly one versioned libpython runtime.*'
# Also exercise the nested ZIP layout used by the final APK, without pretending this is a real APK.
$fakeApk = Join-Path $root 'name-policy-fixture.apk'
[IO.Compression.ZipFile]::CreateFromDirectory($good, $fakeApk)
$zip = [IO.Compression.ZipFile]::Open($fakeApk, [IO.Compression.ZipArchiveMode]::Update)
try { [void]$zip.CreateEntry('lib/arm64-v8a/libffmpeg.so') } finally { $zip.Dispose() }
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $fakeApk -Abi arm64-v8a
$zip = [IO.Compression.ZipFile]::Open($fakeApk, [IO.Compression.ZipArchiveMode]::Update)
try { [void]$zip.CreateEntry('lib/arm64-v8a/libcrypto.so') } finally { $zip.Dispose() }
Assert-Rejected $fakeApk 'System-library shadowing alias:*'
Write-Host "PASS: 13 library-name policy cases. Synthetic fixtures retained at $root"
