param(
    [Parameter(Mandatory)][string]$NativeSourceDirectory,
    [Parameter(Mandatory)][string]$NativeWorkDirectory,
    [Parameter(Mandatory)][string]$NoticesDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$SourceCommit,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$source = (Resolve-Path -LiteralPath $NativeSourceDirectory).Path
$work = (Resolve-Path -LiteralPath $NativeWorkDirectory).Path
$notices = (Resolve-Path -LiteralPath $NoticesDirectory).Path
$archive = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $archive) { throw 'Output already exists. Never replace a published source package.' }
$appSource = Join-Path $source 'LumeFetch-source.zip'
if (Test-Path -LiteralPath $appSource) { throw 'Choose a fresh native source directory.' }
& git -C $repo archive --format=zip --prefix=LumeFetch/ "--output=$appSource" $SourceCommit
if ($LASTEXITCODE -ne 0) { throw 'Cannot archive the exact application source commit.' }
Copy-Item -LiteralPath $notices -Destination (Join-Path $source 'notices') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'packaging/android/REDISTRIBUTION.md') -Destination (Join-Path $source 'README.md')
foreach ($target in @('aarch64-linux-android','x86_64-linux-android')) {
    $record = Join-Path $work "$target/prefix/build-record"
    $outputRecord = Join-Path $source "build-record/$target"
    New-Item -ItemType Directory -Path $outputRecord -Force | Out-Null
    foreach ($name in @('config.h','config.mak','compiler.txt','build-android-native.sh','android-clang.sh','python-launcher.c')) {
        $text = Get-Content -LiteralPath (Join-Path $record $name) -Raw
        if (!$text) { throw "Empty build record: $target/$name" }
        foreach ($mapping in @(@($work, '<WORK>'), @($repo, '<REPO>'))) {
            $windows = $mapping[0].Replace('\','/')
            $msys = '/' + $windows.Substring(0,1).ToLowerInvariant() + $windows.Substring(2)
            $text = $text.Replace($mapping[0], $mapping[1]).Replace($windows, $mapping[1]).Replace($msys, $mapping[1])
        }
        if ($text -match '(?i)(?:[a-z]:|/[a-z])/Users/[^/]+') { throw "Unnormalized local path: $name" }
        [IO.File]::WriteAllText((Join-Path $outputRecord $name), $text)
    }
}
[IO.File]::WriteAllText((Join-Path $source 'SOURCE-COMMIT.txt'), $SourceCommit + "`n")
$sums = foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\','/')
    if ($relative -match '(?i)\.(p12|pfx|jks|keystore|key|dpapi)$') { throw "Private material in source package: $relative" }
    "{0}  {1}" -f (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant(), $relative
}
[IO.File]::WriteAllText((Join-Path $source 'SHA256SUMS.txt'), ($sums -join "`n") + "`n")
New-Item -ItemType Directory -Path (Split-Path $archive -Parent) -Force | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)
Get-FileHash -LiteralPath $archive
Write-Host 'Matching app source, native sources/patches, notices and normalized actual build records packaged. Nothing uploaded.'
