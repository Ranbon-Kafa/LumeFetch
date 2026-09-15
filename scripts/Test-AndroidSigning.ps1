param(
    [Parameter(Mandatory)][string]$ReleaseApk,
    [string]$JavaSdkDirectory = 'C:/Program Files/Android/Android Studio/jbr'
)
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ('LumeFetch-SigningTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$key = Join-Path $work 'TEST-ONLY-NEVER-RELEASE.p12'
$keytool = Join-Path $JavaSdkDirectory 'bin/keytool.exe'
$previousStore = $env:LUMEFETCH_ANDROID_STORE_PASSWORD
$previousKey = $env:LUMEFETCH_ANDROID_KEY_PASSWORD
try {
    $env:LUMEFETCH_ANDROID_STORE_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:LUMEFETCH_ANDROID_KEY_PASSWORD = $env:LUMEFETCH_ANDROID_STORE_PASSWORD
    & $keytool -genkeypair -keystore $key -storetype PKCS12 -alias test-only -keyalg RSA -keysize 2048 -validity 2 `
        -dname 'CN=LumeFetch disposable signing test' -storepass:env LUMEFETCH_ANDROID_STORE_PASSWORD -noprompt
    if ($LASTEXITCODE -ne 0) { throw 'Disposable test-key generation failed.' }
    $pem = @(& $keytool -exportcert -rfc -keystore $key -alias test-only -storepass:env LUMEFETCH_ANDROID_STORE_PASSWORD)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the test certificate.' }
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem(($pem -join "`n"))
    try { $fingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) }
    finally { $certificate.Dispose() }
    $output = Join-Path $work 'TEST-ONLY.apk'
    $arguments = @{Apk=$ReleaseApk; KeyStore=$key; Alias='test-only'; Output=$output; ExpectedCertificateSha256=$fingerprint; JavaSdkDirectory=$JavaSdkDirectory}
    & (Join-Path $PSScriptRoot 'Sign-AndroidPackage.ps1') @arguments
    if (!(Test-Path -LiteralPath $output)) { throw 'Successful signing did not produce output.' }
    $hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Sign-AndroidPackage.ps1') @arguments }
    catch { if ($_.Exception.Message -notlike 'Signing output already exists*') { throw }; $rejected = $true }
    if (!$rejected -or (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash -ne $hash) { throw 'Overwrite protection failed.' }
    $arguments.Output = Join-Path $work 'WRONG-IDENTITY.apk'
    $arguments.ExpectedCertificateSha256 = '0' * 64
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Sign-AndroidPackage.ps1') @arguments }
    catch { if ($_.Exception.Message -notlike 'Wrong signing identity*') { throw }; $rejected = $true }
    if (!$rejected -or (Test-Path -LiteralPath $arguments.Output)) { throw 'Wrong-identity protection failed.' }
    Write-Host 'PASS: valid identity, overwrite refusal, wrong identity rejection. No production key created; no APK installed/published.'
    Write-Host "Disposable test artifacts retained at $work"
} finally {
    $env:LUMEFETCH_ANDROID_STORE_PASSWORD = $previousStore
    $env:LUMEFETCH_ANDROID_KEY_PASSWORD = $previousKey
}
