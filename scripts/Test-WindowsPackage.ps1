param(
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$Portable,
    [string]$PreviousInstaller
)
$ErrorActionPreference = 'Stop'
# This test installs/uninstalls only on disposable GitHub-hosted Windows workers.
if ($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installer acceptance is restricted to a disposable GitHub-hosted runner.'
}
$tempRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
$testRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('lumefetch-package-' + [guid]::NewGuid().ToString('N'))))
if (!$testRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test path escaped runner temp.' }
$installRoot = Join-Path $testRoot 'installed'
$portableRoot = Join-Path $testRoot 'portable'
$settingsDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LumeFetch'
$settingsFile = Join-Path $settingsDirectory 'settings.json'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E531BAA-09C0-4376-B5CE-4B021991669C}_is1'
if ((Test-Path -LiteralPath $uninstallKey) -or (Test-Path -LiteralPath $settingsDirectory)) {
    throw 'Existing LumeFetch installation/data detected. Refusing to alter it.'
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(180000)) { throw "Process timed out: $Executable" }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Process failed ($($process.ExitCode)): $Executable" }
}
function Install-Package([string]$Path) {
    Invoke-Checked ([IO.Path]::GetFullPath($Path)) @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', '/NOICONS', '/TASKS=""', ('/DIR="' + $installRoot + '"'))
    if (!(Test-Path -LiteralPath $uninstallKey)) { throw 'Per-user uninstall registration missing.' }
    if (Test-Path -LiteralPath (Join-Path $installRoot 'portable.flag')) { throw 'Installer incorrectly enables portable mode.' }
    Invoke-Checked (Join-Path $installRoot 'LumeFetch.exe') @('--self-check')
}
if ($PreviousInstaller) { Install-Package $PreviousInstaller } else { Install-Package $Installer }
New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
$downloadRoot = Join-Path $testRoot 'user-downloads'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$sentinel = Join-Path $downloadRoot 'preserve-me.txt'
'LumeFetch package acceptance fixture' | Set-Content -LiteralPath $sentinel -Encoding utf8
@{ DownloadDirectory = $downloadRoot; MaxParallelDownloads = 2; SmartPaste = $true; EmbedMetadata = $true; EmbedThumbnail = $false; Language = 'tr' } |
    ConvertTo-Json | Set-Content -LiteralPath $settingsFile -Encoding utf8
$settingsHash = (Get-FileHash -LiteralPath $settingsFile).Hash
Install-Package $Installer
if ((Get-FileHash -LiteralPath $settingsFile).Hash -ne $settingsHash) { throw 'Upgrade changed user settings.' }
Expand-Archive -LiteralPath $Portable -DestinationPath $portableRoot
if (!(Test-Path -LiteralPath (Join-Path $portableRoot 'portable.flag'))) { throw 'Portable marker missing.' }
Invoke-Checked (Join-Path $portableRoot 'LumeFetch.exe') @('--self-check')
$uninstaller = [IO.Path]::GetFullPath((Join-Path $installRoot 'unins000.exe'))
if (!$uninstaller.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstaller escaped isolated test directory.' }
Invoke-Checked $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
if (Test-Path -LiteralPath (Join-Path $installRoot 'LumeFetch.exe')) { throw 'Uninstall left the installed executable.' }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Uninstall registration was not removed.' }
if ((Get-FileHash -LiteralPath $settingsFile).Hash -ne $settingsHash) { throw 'Uninstall removed or changed user settings.' }
if (!(Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed user downloads.' }
Write-Host 'PASS: per-user installation, reinstallation/upgrade, portable self-check, uninstall, preserved settings/downloads.'
Write-Host 'This is a Windows Server runner check, not Windows 10/11 interactive or SmartScreen certification.'
