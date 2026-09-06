param([string]$MsysRoot = 'C:/msys64', [int]$Jobs = 4, [string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$sources = Join-Path $repo '.tools/release-sources'
New-Item -ItemType Directory -Path $sources -Force | Out-Null
$inputs = @(
    @{ File = 'ffmpeg-9.0.1.tar.xz'; Url = 'https://ffmpeg.org/releases/ffmpeg-9.0.1.tar.xz'; Hash = 'cf38e0e28c7e5605942c4a77755349b0145804a397af37eb1fb4c77cb237f635' },
    @{ File = 'lame-3.100.tar.gz'; Url = 'https://deb.debian.org/debian/pool/main/l/lame/lame_3.100.orig.tar.gz'; Hash = 'ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e' },
    @{ File = 'opus-1.6.1.tar.gz'; Url = 'https://downloads.xiph.org/releases/opus/opus-1.6.1.tar.gz'; Hash = '6ffcb593207be92584df15b32466ed64bbec99109f007c82205f0194572411a1' },
    @{ File = 'zlib-1.3.2.tar.gz'; Url = 'https://zlib.net/zlib-1.3.2.tar.gz'; Hash = 'bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16' }
)
foreach ($input in $inputs) {
    $archive = Join-Path $sources $input.File
    if (!(Test-Path -LiteralPath $archive)) { Invoke-WebRequest -Uri $input.Url -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $input.Hash) { throw "Source checksum mismatch: $($input.File)" }
}
$bash = Join-Path $MsysRoot 'usr/bin/bash.exe'
if (!(Test-Path -LiteralPath $bash)) { throw 'MSYS2 with MinGW64 GCC, make and pkg-config is required.' }
$env:LUMEFETCH_BUILD_REPO = $repo
$env:LUMEFETCH_BUILD_JOBS = [Math]::Clamp($Jobs, 1, 16)
if (!$WorkDirectory) { $WorkDirectory = Join-Path $repo '.tools/native-build' }
if ($WorkDirectory -match '[^\x20-\x7E]| ') { throw 'Choose an ASCII work directory without spaces using -WorkDirectory (MinGW pkg-config limitation).' }
New-Item -ItemType Directory -Path $WorkDirectory -Force | Out-Null
$env:LUMEFETCH_NATIVE_WORK = [IO.Path]::GetFullPath($WorkDirectory)
& $bash --noprofile --norc -c 'export PATH=/mingw64/bin:/usr/bin; cd "$(cygpath -u "$LUMEFETCH_BUILD_REPO")"; bash scripts/build-ffmpeg.sh'
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg source build failed.' }
Write-Host 'Built .tools/ffmpeg-lumefetch; original preview tools are unchanged.'
