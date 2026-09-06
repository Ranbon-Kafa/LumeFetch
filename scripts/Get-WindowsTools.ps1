param([switch]$IncludeInstallerCompiler, [switch]$SkipFFmpeg)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = Split-Path $PSScriptRoot -Parent
$toolRoot = Join-Path $repo '.tools'
$manifest = Get-Content (Join-Path $repo 'packaging/windows/dependencies.json') -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path (Join-Path $toolRoot 'archives') | Out-Null
foreach ($name in @('yt-dlp', 'deno', 'inno')) {
    if ($name -eq 'inno' -and !$IncludeInstallerCompiler) { continue }
    $dependency = $manifest.$name
    $extension = if ($name -in @('yt-dlp', 'inno')) { '.exe' } else { '.zip' }
    $archive = Join-Path $toolRoot "archives/$name-$($dependency.version)$extension"
    if (!(Test-Path -LiteralPath $archive)) {
        Write-Host "Downloading $name $($dependency.version)"
        Invoke-WebRequest -Uri $dependency.url -OutFile $archive
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $dependency.sha256) {
        throw "Checksum mismatch for $name. The file was not executed or extracted."
    }
    $destination = Join-Path $toolRoot $name
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    if ($name -eq 'yt-dlp') {
        Copy-Item -LiteralPath $archive -Destination (Join-Path $destination 'yt-dlp.exe') -Force
    } elseif ($name -eq 'inno') {
        if (!(Test-Path -LiteralPath (Join-Path $destination 'ISCC.exe'))) {
            $compiler = Start-Process -FilePath $archive -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', '/TASKS=""', ('/DIR="' + $destination + '"')) -WindowStyle Hidden -PassThru -Wait
            if ($compiler.ExitCode -ne 0) { throw "Compiler setup failed: $($compiler.ExitCode)" }
        }
    } elseif ($name -eq 'deno') {
        Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
    }
    Write-Host "Verified $name $($dependency.version)"
}
$licenseRoot = Join-Path $toolRoot 'licenses'
if (!$SkipFFmpeg) { Write-Host 'FFmpeg is built from source: run scripts/Build-FFmpeg.ps1 with MSYS2/MinGW64.' }
New-Item -ItemType Directory -Force -Path $licenseRoot | Out-Null
$licenses = @{
    'yt-dlp-LICENSE.txt' = 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/LICENSE'
    'yt-dlp-THIRD_PARTY_LICENSES.txt' = 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/THIRD_PARTY_LICENSES.txt'
    'deno-LICENSE.txt' = 'https://raw.githubusercontent.com/denoland/deno/v2.9.6/LICENSE.md'
    'Avalonia-LICENSE.txt' = 'https://raw.githubusercontent.com/AvaloniaUI/Avalonia/d3c867a9e2de379249b03dbeb3495bd7f076a81a/licence.md'
    'Inter-LICENSE.txt' = 'https://raw.githubusercontent.com/rsms/inter/v4.1/LICENSE.txt'
    'SkiaSharp-LICENSE.txt' = 'https://raw.githubusercontent.com/mono/SkiaSharp/f568ac94dd768ef9a2f593537cfde2dd0d348ef5/LICENSE.txt'
}
foreach ($entry in $licenses.GetEnumerator()) {
    Invoke-WebRequest -Uri $entry.Value -OutFile (Join-Path $licenseRoot $entry.Key)
}
