param(
    [Parameter(Mandatory)][string]$KeyStore,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Alias = 'lumefetch',
    [switch]$GenerateProtectedPassword,
    [string]$JavaSdkDirectory = 'C:/Program Files/Android/Android Studio/jbr'
)
$ErrorActionPreference = 'Stop'
$path = [IO.Path]::GetFullPath($KeyStore)
$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if ($path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase) -or
    ($env:OneDrive -and $path.StartsWith($env:OneDrive.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Keep the signing key outside the source checkout and automatic OneDrive sync. Use a private local folder and a deliberate encrypted backup.'
}
if (Test-Path -LiteralPath $path) { throw 'The target exists. Existing signing identities are never overwritten.' }
$keytool = Join-Path $JavaSdkDirectory 'bin/keytool.exe'
if (!(Test-Path -LiteralPath $keytool)) { throw 'JDK keytool is required.' }
$privateDirectory = Split-Path $path -Parent
$protectedPath = $path + '.password.dpapi'
if ($GenerateProtectedPassword -and (!$IsWindows -or (Test-Path -LiteralPath $privateDirectory))) {
    throw 'Automatic password protection requires Windows and a NEW dedicated private directory. Existing directories are not altered.'
}
$password = $null
$confirmation = $null
$previousPassword = $env:LUMEFETCH_NEW_KEY_PASSWORD
try {
    if ($GenerateProtectedPassword) {
        # Protect the directory BEFORE any secret is written. No inherited access.
        New-Item -ItemType Directory -Path $privateDirectory | Out-Null
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,
            'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
        Set-Acl -LiteralPath $privateDirectory -AclObject $acl
        $random = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
        $plain = [Convert]::ToBase64String($random)
        [Array]::Clear($random)
        $password = ConvertTo-SecureString $plain -AsPlainText -Force
        # Windows DPAPI: ciphertext is tied to this Windows user on this computer.
        $encrypted = ConvertFrom-SecureString $password
        $stream = [IO.File]::Open($protectedPath, [IO.FileMode]::CreateNew)
        $writer = [IO.StreamWriter]::new($stream)
        try { $writer.Write($encrypted) } finally { $writer.Dispose() }
    } else {
        $password = Read-Host 'New signing password (at least 12 characters; keep a separate backup)' -AsSecureString
        $confirmation = Read-Host 'Confirm password' -AsSecureString
        $plain = [Net.NetworkCredential]::new('', $password).Password
        $repeat = [Net.NetworkCredential]::new('', $confirmation).Password
        if ($plain.Length -lt 12 -or $plain -cne $repeat) { throw 'Passwords must match and contain at least 12 characters.' }
        New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
    }
    # Credentials are inherited only by keytool, not written into arguments or build logs.
    $env:LUMEFETCH_NEW_KEY_PASSWORD = $plain
    & $keytool -genkeypair -keystore $path -storetype PKCS12 -alias $Alias -keyalg RSA -keysize 4096 `
        -validity 10000 -dname 'CN=LumeFetch, OU=Android, O=ruzgarefe.com' `
        -storepass:env LUMEFETCH_NEW_KEY_PASSWORD -keypass:env LUMEFETCH_NEW_KEY_PASSWORD
    if ($LASTEXITCODE -ne 0) { throw 'Key creation failed. Inspect the target before retrying; it will not be overwritten.' }
    if ($IsWindows) {
        $acl = Get-Acl -LiteralPath $path
        $acl.SetAccessRuleProtection($true, $false)
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'Allow'))
        Set-Acl -LiteralPath $path -AclObject $acl
    }
    & $keytool -list -v -keystore $path -alias $Alias -storepass:env LUMEFETCH_NEW_KEY_PASSWORD
    if ($LASTEXITCODE -ne 0) { throw 'Created key could not be verified.' }
    # Only the public certificate is exported. It can safely identify future updates.
    & $keytool -exportcert -keystore $path -alias $Alias -storepass:env LUMEFETCH_NEW_KEY_PASSWORD -file ($path + '.cer')
    if ($LASTEXITCODE -ne 0) { throw 'Public certificate export failed.' }
    Get-FileHash -LiteralPath ($path + '.cer') -Algorithm SHA256
    if ($GenerateProtectedPassword) {
        Write-Host "Password stored with Windows DPAPI: $protectedPath"
        Write-Warning 'DPAPI is NOT a portable backup. Before replacing Windows or this PC, export the password privately to a password manager and separately back up the keystore. Never send either through chat or GitHub.'
    }
    Write-Host 'Keep the keystore AND password in separate secure backups. Losing either prevents future APK updates.'
    Write-Host 'The certificate fingerprint is public; never commit the private keystore or password.'
} finally {
    $env:LUMEFETCH_NEW_KEY_PASSWORD = $previousPassword
    $plain = $null
    $repeat = $null
    if ($password) { $password.Dispose() }
    if ($confirmation) { $confirmation.Dispose() }
}
