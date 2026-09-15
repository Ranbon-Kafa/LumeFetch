param(
    [ValidateSet('arm64-v8a', 'x86_64')][string]$Abi = 'arm64-v8a',
    [Parameter(Mandatory)][string]$WorkDirectory,
    [Parameter(Mandatory)][string]$NdkDirectory,
    [int]$Jobs = 4,
    [switch]$ResumeConfiguredFFmpeg
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$work = [IO.Path]::GetFullPath($WorkDirectory)
if ($work -match '[^\x20-\x7E]| ') { throw 'Native work directory must be an ASCII path without spaces.' }
$target = if ($Abi -eq 'arm64-v8a') { 'aarch64-linux-android' } else { 'x86_64-linux-android' }
$inputs = @(
    @{ File = '.tools/release-sources/ffmpeg-9.0.1.tar.xz'; Hash = 'cf38e0e28c7e5605942c4a77755349b0145804a397af37eb1fb4c77cb237f635' },
    @{ File = '.tools/release-sources/lame-3.100.tar.gz'; Hash = 'ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e' },
    @{ File = '.tools/release-sources/opus-1.6.1.tar.gz'; Hash = '6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1' },
    @{ File = '.tools/release-sources/zlib-1.3.2.tar.gz'; Hash = 'bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16' },
    @{ File = '.tools/android-native/downloads/quickjs-2026-06-04.tar.xz'; Hash = 'b376e839b322978313d929fd20663b11ba58b75df5a46c126dd19ea2fa70ad2a' },
    @{ File = ".tools/android-native/downloads/python-3.14.7-$target.tar.gz"; Hash = $(if ($Abi -eq 'arm64-v8a') {
            '6d50cc3aa66e414a439594089bcdfb5f1264358155c70c1f00471c24cfb477fb'
        } else { '2c16ce2359565cd8c24f86cfb75630768ba6607e732946b294b969797f583b60' }) }
)
foreach ($input in $inputs) {
    if ((Get-FileHash -LiteralPath (Join-Path $repo $input.File) -Algorithm SHA256).Hash -ne $input.Hash) {
        throw "Source checksum mismatch: $($input.File)"
    }
}
if (!(Test-Path -LiteralPath (Join-Path $NdkDirectory 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'))) {
    throw 'A verified Windows Android NDK is required.'
}
New-Item -ItemType Directory -Path $work -Force | Out-Null
$python = Join-Path $work "python-$target"
if (!(Test-Path -LiteralPath (Join-Path $python '.extraction-complete'))) {
    New-Item -ItemType Directory -Path $python -Force | Out-Null
    # Materialize the three known in-prefix symlinks as copies on Windows.
    & tar -xzf (Join-Path $repo ".tools/android-native/downloads/python-3.14.7-$target.tar.gz") -C $python `
        --exclude='./prefix/lib/libsqlite3.so.0' `
        --exclude='./prefix/lib/pkgconfig/python3-embed.pc' `
        --exclude='./prefix/lib/pkgconfig/python3.pc'
    if ($LASTEXITCODE -ne 0) { throw 'Official Python Android extraction failed.' }
    Copy-Item -LiteralPath (Join-Path $python 'prefix/lib/libsqlite3_python.so') -Destination (Join-Path $python 'prefix/lib/libsqlite3.so.0')
    Copy-Item -LiteralPath (Join-Path $python 'prefix/lib/pkgconfig/python-3.14-embed.pc') -Destination (Join-Path $python 'prefix/lib/pkgconfig/python3-embed.pc')
    Copy-Item -LiteralPath (Join-Path $python 'prefix/lib/pkgconfig/python-3.14.pc') -Destination (Join-Path $python 'prefix/lib/pkgconfig/python3.pc')
    New-Item -ItemType File -Path (Join-Path $python '.extraction-complete') | Out-Null
}
$env:LUMEFETCH_BUILD_REPO = $repo
$env:LUMEFETCH_NATIVE_WORK = $work
$env:LUMEFETCH_ANDROID_NDK = [IO.Path]::GetFullPath($NdkDirectory)
$env:LUMEFETCH_ANDROID_TARGET = $target
$env:LUMEFETCH_BUILD_JOBS = [Math]::Clamp($Jobs, 1, 8)
$env:LUMEFETCH_RESUME_FFMPEG = if ($ResumeConfiguredFFmpeg) { '1' } else { '0' }
# Bash reads scripts incrementally. Freeze them so editing the checkout cannot
# change a running build's byte offsets or commands.
$scriptSnapshot = Join-Path $work ('recipe-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scriptSnapshot | Out-Null
foreach ($file in @('build-android-native.sh','android-clang.sh')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $scriptSnapshot
}
Copy-Item -LiteralPath (Join-Path $repo 'packaging/android/native/python-launcher.c') -Destination $scriptSnapshot
$env:LUMEFETCH_NATIVE_SCRIPT = Join-Path $scriptSnapshot 'build-android-native.sh'
& 'C:/msys64/usr/bin/bash.exe' --noprofile --norc -c 'export PATH=/mingw64/bin:/usr/bin; bash "$(cygpath -u "$LUMEFETCH_NATIVE_SCRIPT")"'
if ($LASTEXITCODE -ne 0) { throw "Android native build failed. Intermediates retained at $work" }
Write-Host "Native outputs: $work/$target/prefix"
Write-Host 'Existing APK inputs are unchanged. Native acceptance and source/notice packaging are still required.'
