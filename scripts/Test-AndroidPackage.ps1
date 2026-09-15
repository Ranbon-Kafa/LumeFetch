param(
    [Parameter(Mandatory)][string]$Apk,
    [ValidateSet('arm64-v8a', 'x86_64')][Parameter(Mandatory)][string]$Abi,
    [string]$BundledToolsDirectory,
    [string]$ExpectedVersion,
    [int]$ExpectedVersionCode = 0,
    [ValidatePattern('^$|^[a-fA-F0-9]{64}$')][string]$ExpectedCertificateSha256,
    [switch]$RequireReleaseNotices,
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA/Android/Sdk",
    [string]$JavaSdkDirectory = 'C:/Program Files/Android/Android Studio/jbr'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
if (!$BundledToolsDirectory) { $BundledToolsDirectory = Join-Path $repo '.tools/android-backend/prepared' }
$preparedManifest = Join-Path $BundledToolsDirectory 'assets/tools/manifest.json'
$expected = Get-Content -LiteralPath $preparedManifest -Raw | ConvertFrom-Json
$archive = [IO.Compression.ZipFile]::OpenRead($apkPath)
try {
    function Get-EntryHash([string]$name) {
        $entry = $archive.GetEntry($name)
        if (!$entry) { throw "APK is missing $name" }
        $stream = $entry.Open()
        try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    if ((Get-EntryHash 'assets/tools/manifest.json') -ne (Get-FileHash -LiteralPath $preparedManifest -Algorithm SHA256).Hash.ToLowerInvariant()) {
        throw 'APK runtime manifest does not match the prepared, checksum-pinned inputs.'
    }
    foreach ($file in $expected.files | Where-Object { $_.abi -eq 'any' -or $_.abi -eq $Abi }) {
        $entryName = if ($file.kind -eq 'native') { "lib/$Abi/$($file.name)" } else { "assets/$($file.asset)" }
        if ((Get-EntryHash $entryName) -ne $file.sha256) { throw "Bundled tool mismatch: $entryName" }
    }
    if ((Get-EntryHash 'assets/tools/bootstrap.py') -ne (Get-FileHash -LiteralPath (Join-Path $repo 'src/LumeFetch.Android/Assets/tools/bootstrap.py')).Hash.ToLowerInvariant()) {
        throw 'APK bootstrap does not match its source.'
    }
    if (@($archive.Entries | Where-Object { $_.FullName -match '^lib/([^/]+)/' -and $Matches[1] -ne $Abi }).Count -gt 0) {
        throw 'Unexpected ABI in single-architecture APK.'
    }
    if ($RequireReleaseNotices) {
        $notices = Join-Path $BundledToolsDirectory 'assets/notices/third-party-notices.zip'
        if ((Get-EntryHash 'assets/notices/third-party-notices.zip') -ne (Get-FileHash -LiteralPath $notices).Hash.ToLowerInvariant()) {
            throw 'The APK must contain the exact reviewed third-party notice archive.'
        }
        if ((Get-EntryHash 'assets/notices/INDEX.md') -ne (Get-FileHash -LiteralPath (Join-Path $BundledToolsDirectory 'assets/notices/INDEX.md')).Hash.ToLowerInvariant()) {
            throw 'Missing/mismatched source access information.'
        }
    }
} finally { $archive.Dispose() }
& (Join-Path $PSScriptRoot 'Test-AndroidLibraryIsolation.ps1') -Path $apkPath -Abi $Abi
$signer = Get-ChildItem -LiteralPath (Join-Path $AndroidSdkDirectory 'build-tools') -Directory |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'apksigner.bat' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$signer) { throw 'Android apksigner is required to verify the package signature.' }
$env:JAVA_HOME = $JavaSdkDirectory
$verification = @(& $signer verify --verbose --print-certs $apkPath)
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
$verification
if ($ExpectedCertificateSha256) {
    $actual = [regex]::Match(($verification -join "`n"), '(?m)certificate SHA-256 digest:\s*([a-fA-F0-9]{64})').Groups[1].Value
    if ($actual -ine $ExpectedCertificateSha256 -or ($verification -join "`n") -match 'CN=Android Debug') { throw 'Wrong release identity.' }
}
if ($ExpectedVersion -or $ExpectedVersionCode -or $ExpectedCertificateSha256) {
    $badging = @(& (Join-Path (Split-Path $signer -Parent) 'aapt2.exe') dump badging $apkPath) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $badging -notmatch "package: name='com\.ruzgarefe\.lumefetch'" -or $badging -match 'application-debuggable') {
        throw 'Expected a non-debuggable LumeFetch release APK.'
    }
    if ($ExpectedVersion -and $badging -notmatch ("versionName='" + [regex]::Escape($ExpectedVersion) + "'")) { throw 'Wrong display version.' }
    if ($ExpectedVersionCode -and $badging -notmatch "versionCode='$ExpectedVersionCode'") { throw 'Wrong version code.' }
}
Get-FileHash -LiteralPath $apkPath -Algorithm SHA256
Write-Host 'PASS: signature, ABI, native tools, Python/FFmpeg payloads, yt-dlp, bootstrap and manifest integrity.'
Write-Host 'This check does not certify feature parity, Android device support or redistribution compliance.'
