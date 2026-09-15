param(
    [Parameter(Mandatory)][string]$Apk,
    [Parameter(Mandatory)][string]$KeyStore,
    [Parameter(Mandatory)][string]$Alias,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedCertificateSha256,
    [string]$ProtectedPasswordFile,
    [string]$AndroidSdkDirectory = "$env:LOCALAPPDATA/Android/Sdk",
    [string]$JavaSdkDirectory = 'C:/Program Files/Android/Android Studio/jbr'
)
$ErrorActionPreference = 'Stop'
function Invoke-VerifiedSigning {
$source = (Resolve-Path -LiteralPath $Apk).Path
$key = (Resolve-Path -LiteralPath $KeyStore).Path
$destination = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $destination) { throw 'Signing output already exists; it will not be overwritten.' }
if (!$env:LUMEFETCH_ANDROID_STORE_PASSWORD -or !$env:LUMEFETCH_ANDROID_KEY_PASSWORD) {
    throw 'Set LUMEFETCH_ANDROID_STORE_PASSWORD and LUMEFETCH_ANDROID_KEY_PASSWORD in this local session. Do not put passwords in command arguments.'
}
$signer = Get-ChildItem -LiteralPath (Join-Path $AndroidSdkDirectory 'build-tools') -Directory |
    Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'apksigner.bat' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$signer) { throw 'Android apksigner is required.' }
$env:JAVA_HOME = $JavaSdkDirectory
$aapt = Join-Path (Split-Path $signer -Parent) 'aapt2.exe'
$badging = @(& $aapt dump badging $source)
if ($LASTEXITCODE -ne 0 -or ($badging -join "`n") -notmatch "package: name='com\.ruzgarefe\.lumefetch'" -or
    ($badging -join "`n") -match 'application-debuggable') {
    throw 'Only a non-debuggable LumeFetch Release APK may be signed by this script.'
}
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
$temporary = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.unverified'
& $signer sign --ks $key --ks-key-alias $Alias --ks-pass env:LUMEFETCH_ANDROID_STORE_PASSWORD `
    --key-pass env:LUMEFETCH_ANDROID_KEY_PASSWORD --v1-signing-enabled false --v2-signing-enabled true `
    --v3-signing-enabled true --v4-signing-enabled false --out $temporary $source
if ($LASTEXITCODE -ne 0) { throw "Signing failed. Temporary output retained: $temporary" }
$verification = @(& $signer verify --verbose --print-certs $temporary)
if ($LASTEXITCODE -ne 0) { throw "Signature verification failed. Temporary output retained: $temporary" }
$actual = [regex]::Match(($verification -join "`n"), '(?m)certificate SHA-256 digest:\s*([a-fA-F0-9]{64})').Groups[1].Value
if ($actual -ine $ExpectedCertificateSha256 -or ($verification -join "`n") -match 'CN=Android Debug' -or
    ($verification -join "`n") -notmatch 'Number of signers: 1(?:\r?\n|$)') {
    throw "Wrong signing identity. Unverified output retained: $temporary"
}
Move-Item -LiteralPath $temporary -Destination $destination
$verification
Get-FileHash -LiteralPath $destination -Algorithm SHA256
Write-Host 'Signed and identity-verified only. This does not approve distribution or publish a release.'
}
$previousStorePassword = $env:LUMEFETCH_ANDROID_STORE_PASSWORD
$previousKeyPassword = $env:LUMEFETCH_ANDROID_KEY_PASSWORD
$previousJavaHome = $env:JAVA_HOME
$protectedPassword = $null
try {
    if ($ProtectedPasswordFile) {
        if (!$IsWindows) { throw 'The protected password file requires the original Windows user/computer.' }
        $protectedPassword = Get-Content -LiteralPath $ProtectedPasswordFile -Raw | ConvertTo-SecureString
        $env:LUMEFETCH_ANDROID_STORE_PASSWORD = [Net.NetworkCredential]::new('', $protectedPassword).Password
        $env:LUMEFETCH_ANDROID_KEY_PASSWORD = $env:LUMEFETCH_ANDROID_STORE_PASSWORD
    }
    Invoke-VerifiedSigning
} finally {
    $env:LUMEFETCH_ANDROID_STORE_PASSWORD = $previousStorePassword
    $env:LUMEFETCH_ANDROID_KEY_PASSWORD = $previousKeyPassword
    $env:JAVA_HOME = $previousJavaHome
    if ($protectedPassword) { $protectedPassword.Dispose() }
}
