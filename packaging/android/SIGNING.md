# Android signing and device acceptance

iOS is on hold. Target acceptance phone reported by the maintainer: **Samsung
Galaxy S25 Ultra, Android 16**. On 2026-09-10 the maintainer confirmed both MP3
and MP4 downloads work in build 7. See ACCEPTANCE.md for the remaining beta matrix.

## Signing identity

Use one long-lived private release key for future updates. Current development
APKs use Android Debug signing, including Release-mode test builds. Changing the
certificate prevents installing an update over those test builds; uninstalling
them deletes private settings, queue state and unfinished transfers. Files already
exported to Documents remain outside private app storage. Do not uninstall a
user's build automatically.

The maintainer authorized a new permanent identity, created 2026-09-10. It is
stored locally outside this checkout and OneDrive, with an owner-only Windows
ACL. No key or password is in GitHub. Public certificate: RSA 4096, alias
`lumefetch`, subject `CN=LumeFetch, OU=Android, O=ruzgarefe.com`, SHA-256:

```text
D9A78375002A9948147BF17E03F9E7E60840476E56DAA4BBB0C4B782AC376B90
```

Never paste passwords or private key files in chat. For a NEW identity on a
different project, run locally, outside the repository/OneDrive directory:

```powershell
./scripts/New-AndroidSigningKey.ps1 -KeyStore <private-local-path>/lumefetch.p12
```

The script prompts privately for a password, refuses to overwrite existing keys,
restricts the new file's Windows ACL and prints the public SHA-256 certificate
fingerprint. Keep separately protected backups of the key and password. The
script does not back them up for you.

`-GenerateProtectedPassword` instead creates a random password in a NEW dedicated
private directory, restricts access before writing secrets, and stores the
password as Windows DPAPI ciphertext in `<keystore>.password.dpapi`. This file
requires the original Windows user/computer: **it is not a portable backup**.
The maintainer must privately export the password to a password manager and
make a separately encrypted keystore backup before replacing Windows/the PC.
No off-device backup has been performed automatically. Do not rotate the release
identity merely because a password is inconvenient to retrieve.

Build with an increasing Android version code (never reset it for a release):

```powershell
./scripts/Build-Android.ps1 -Runtime android-arm64 -Configuration Release -Version 1.1.0-beta.1 -VersionCode 10 -BundledToolsDirectory <prepared-runtime>
```

Set `LUMEFETCH_ANDROID_STORE_PASSWORD` and `LUMEFETCH_ANDROID_KEY_PASSWORD` only in
the local signing session. Use the verified public fingerprint, then:

```powershell
./scripts/Sign-AndroidPackage.ps1 -Apk <built-apk> -KeyStore <private-key> -Alias lumefetch -ExpectedCertificateSha256 <fingerprint> -Output <new-apk-path>
```

The signer rejects debuggable/wrong-app inputs, creates a separate output and
checks the expected single signing identity. It passes passwords through process
environment variables, not command arguments or log text. Clear those session
variables afterward. Signing does **not** clear the source/license or device
acceptance gates, and no script publishes an APK automatically.

With local DPAPI storage, pass `-ProtectedPasswordFile <keystore>.password.dpapi`
instead of setting password environment variables. The signer decrypts only in
memory, passes secrets to the child process environment, then restores prior
environment values in `finally`, even if verification fails.

## S25 Ultra acceptance checklist

- Install the reviewed ARM64 APK; verify displayed version and offline runtime
  initialization. Allow only the requested folder grant and notifications.
- Test authorized single-media and playlist sources, MP4/MP3 choices, metadata
  and compatible thumbnail embedding. Review per-item errors.
- Test pause/resume, cancellation/retry, parallelism, screen-off/background,
  rotation, force-stop/reopen and completed history.
- Revoke a folder grant and test a new download: it must fail clearly before
  transferring. Select the folder again to recover. Test full/offline storage.
- Verify final files using another player and confirm no duplicate output after
  interrupted export. Keep an interrupted-export row for manual review.
- Test Turkish/English, system sharing, keyboard/safe-area layout and notification
  denial. Record Android build/version, page size and test results, not user URLs
  or private queue/settings data, in public diagnostics.

Record failures before calling the APK stable. The private website is out of scope.
