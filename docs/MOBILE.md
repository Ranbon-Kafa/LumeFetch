# Android / iOS adaptation

Status (2026-09-15): **Android 1.1.0-beta.1**, ARM64 APK, version code 10.
The maintainer confirmed build 7 MP3/MP4 on S25 Ultra / Android 16. Beta 1 keeps
those runtime bytes, removes the temporary diagnostic settings card, includes
the complete collected notices and uses a permanent signing identity.
See [release files](https://github.com/Ranbon-Kafa/LumeFetch/releases/tag/android-v1.1.0-beta.1),
[acceptance record](../packaging/android/ACCEPTANCE.md),
[source/build instructions](../packaging/android/NATIVE-BUILD.md) and
[licenses](../packaging/android/REDISTRIBUTION.md).

iOS is explicitly on hold. Windows v1.0.0 release assets are unchanged. The
feature surface is shared, but full mobile device/provider parity is not claimed.

## Product contract

The target is the existing v1 feature set, not a smaller mobile edition:

- YouTube/YouTube Music, Instagram, TikTok, X, Reddit and generic media routing.
- Smart Paste, analysis, thumbnails, quality/format selection and real MP3 encoding.
- YouTube playlists, batch quality, per-item failures and confirmation.
- Queue, progress/speed/remaining time, pause/resume, cancel/retry and parallelism.
- Metadata, compatible cover embedding, settings, Turkish/English and branding.
- Runtime tools packaged with the app: no first-launch tool downloads.
- Spotify stays disabled, as in Windows v1. No website repository changes.

## Architecture implemented

`LumeFetch.Presentation` contains the original views, view models and theme and
references Core/portable Avalonia only. Desktop and Android host the same MainView,
commands, providers and DownloadManager. Navigation/forms reflow for phone widths.
Android retains its root while loading/content changes; safe area padding keeps
content outside system bars. Compiled bindings and source-generated settings/
provider-plan JSON reduce reflection dependencies.

Portable boundaries:

- `ISettingsStore`, host defaults and `ICatalogSession` (Spotify remains disabled).
- `ToolCommand`: literal arguments, child environment and platform cancellation.
- `IDownloadOutput`: export completed transfers to platform storage. Failed or
  canceled export retains the local file for retry without redownloading. Output
  URIs and human-readable paths are separate.
- `IDownloadQueueStore`: optional private, versioned queue checkpoints. Android
  restores history and unfinished jobs with their original identity. Interrupted
  exports are review-only; new work is blocked after a journal read/write error.

Android host (`src/LumeFetch.Android`):

- .NET 10 / Avalonia 12.1.2, Android API 26 minimum, arm64/x86_64 development
  packages. The Android project is separate from the desktop solution.
- Embedded Python, FFmpeg/FFprobe, QuickJS and official yt-dlp 2026.08.19 zipapp
  with EJS resources. Inputs pinned in `packaging/android/tools.lock.json`.
- Native executables run from Android's installer-owned library directory.
  Runtime data is verified and extracted into a versioned private no-backup cache
  through a sibling staging directory. Archive paths, symlinks and sizes are checked.
- Python has a private POSIX process group so cancellation terminates its child
  FFmpeg/JavaScript processes without killing the app's process group.
- Storage Access Framework folder picker/persisted grants, without all-files
  permission. Select a folder before enqueueing; save settings to retain it.
- Exports create a fresh document with a randomized filename suffix. Failed or
  canceled copies remove only their new document and retain the private input.
  SAF is not a filesystem-wide atomic transaction: a partial document can briefly
  be visible and remote-provider cleanup can fail. These remain release tests.
- Android `ACTION_SEND text/plain` accepts an HTTP(S) URL for analysis, not
  arbitrary tool arguments or automatic download authority.
- A non-exported data-sync foreground service shows counts, requests notification
  permission and holds a bounded partial wake lock during active transfers.
  Service/notification/wake lock stop when idle. Android service timeout pauses
  transfers/cancels processing with an explanation. This does not bypass OS
  restrictions or guarantee survival after force-stop/process death.

## Development verification history (before beta packaging)

### Shared / Windows

- 100 unit cases, including queue restoration, history/format round trips,
  corrupt/unsupported journals, path rejection, failed writes, ambiguous exports,
  export retry without redownload and invalid grants. Reflection JSON disabled.
- Headless 320/360/390/430/768/1220px UI checks, MP3/playlist/queue commands,
  live TR/EN and Spotify disabled.
- Real generated-media Windows checks: HTTP hashes, queue, yt-dlp progress,
  FFmpeg mux/M4A/MP3/Opus/PNG cover and metadata.

### Android 16 x86_64 emulator (API 36)

- Debug APK installation, verified runtime initialization and actual shared UI.
- Opt-in debug native smoke: generated video, FFmpeg/FFprobe, Opus, HTTP SHA-256,
  queue MP3, Python/yt-dlp metadata and MP3 post-processing, QuickJS.
- Shared URL -> analysis -> MP3 -> native folder grant -> completed queue ->
  file visible in Documents. Export independently probed as real MP3, ~4 seconds.
- A 9,475,677-byte generated MP4 paused/resumed using HTTP 206, then finished
  while the app was backgrounded. Output SHA-256 matched the source:
  `4c90b1ebf08dd942fcefa7aea9fa211adf401d84e6eeb08cbf2331d35e81864f`.
  Foreground service/wake lock existed during transfer, not after completion.
- Force-stop during a new 9,475,677-byte HTTP transfer: reopened with the same
  job ID and paused progress, no automatic request. Manual resume used HTTP 206;
  exported bytes matched the same source hash above. A second restart preserved
  completed history. Evidence: `artifacts/android/emulator/queue-recovered.png`
  and `process-recovered.mp4` (generated test content).
- English -> Turkish live switch and saved language/default folder.
- Release with trimming/AOT builds and opens. Settings/grant survive installing
  over Debug; real UI MP3 export also works in Release. The debug smoke hook is
  excluded from Release.
- arm64 Debug builds. Embedded manifest/bootstrap/tool hashes, ABI and APK v2/v3
  signatures checked. All current packages use **local Android Debug signing**,
  not a public release identity (including the Release configuration).

These are not physical-device certification, live-platform acceptance, an
endurance test or iOS verification. Ignored `artifacts/android/emulator` contains
actual emulator captures and fixture output, not marketing screenshots.

## Historical native replacement work (September 9–10)

### 2026-09-09: source-built replacement and phone test APK

Both native ABIs now build from our FFmpeg/QuickJS recipes and official CPython
3.14.7. The replacement passes the emulator native smoke, including queue MP3,
Opus, extractor post-processing, private stdout, EJS import, Mutagen and the TLS
CA store. The ARM64 Release APK builds without warnings/errors and passes its
signature/tool-manifest checks and all **180** ELF alignment checks. This is
static 16-KB compatibility, not a physical 16-KB device test.

The S25 Ultra device-test APK is a locally debug-signed **Release configuration**
build, version `1.1.0-dev`, code 3, 65,490,338 bytes. It is not a public release.
SHA-256: `139e9d43432543e39e304d6d2940a9e3c0a214bbd07ccd899f9a7f019a859d3e`.
Local output: `artifacts/android/device-test-20260909/LumeFetch-Android-1.1.0-device-test-arm64.apk`.

**Physical test follow-up:** the maintainer's S25 Ultra screenshot reports
FFmpeg unavailable and MP3/MP4 post-processing failures. Diagnostic build 6 exposed
the cause: Python SDK `libcrypto.so` shadowed Samsung's system crypto library.
Build 7 excludes the SDK aliases, retaining private `*_python.so` runtime names
and TLS verification. A new build/APK/CI policy prevents this regression. Both
native ABIs pass dependency/isolation checks; x86_64 native MP3, post-processing,
TLS and SQLite tests pass. ARM64 build 7 passes 177 static ELF checks. The
maintainer subsequently confirmed MP3 and MP4 on S25. See the acceptance record.

The old AAR runtime is retained only for historical comparison; do **not** use it.
Build-Android now requires an explicit source-built `-BundledToolsDirectory`.
The beta includes native sources, collected notices and permanent release signing.
Wider provider/device and long lifecycle acceptance remains open, so it is not
labelled stable. Spotify remains disabled and iOS remains on hold.

Windows prerequisites: .NET SDK from `global.json`, `android` workload, Android
SDK API 36/build-tools and compatible JDK. These are installed on the current
workstation; fresh developer machines must install them separately.

```powershell
dotnet workload install android
# Prepare a source-built runtime using packaging/android/NATIVE-BUILD.md first.
./scripts/Build-Android.ps1 -Runtime android-arm64 -BundledToolsDirectory <runtime>
./scripts/Build-Android.ps1 -Runtime android-x64 -Configuration Release -BundledToolsDirectory <x64-runtime>
./scripts/Test-AndroidPackage.ps1 -Apk <path-to-apk> -Abi arm64-v8a -BundledToolsDirectory <runtime>
```

Tool downloads occur at **build time**, with pinned SHA-256 verification. Use
`-Offline` after caches are populated. Build-Android snapshots source into an
ASCII temporary directory for Windows Android tools' Unicode-path limitation;
it never moves the checkout. Intermediates are retained for diagnosis, APKs go to
`artifacts/android/<runtime>/<build-id>`. Override SDK/JDK paths using its named
parameters. No script pushes to GitHub or creates a public release.

For a Debug emulator, start MainActivity with boolean intent extra
`lumefetch.runtime-smoke=true`. Resolve the activity component with Android's
package manager. Read app-private `files/runtime-smoke-result.txt` using
`adb shell run-as` and `LumeFetchSmoke` logs. The hook is absent from Release.
Generated fixtures use a temporary loopback server, not third-party content.

`scripts/Serve-MobileFixture.py <generated-video.mp4>` serves only that file,
rate-limited with range support, on 127.0.0.1:8128 (emulator host: 10.0.2.2).
This is a local test fixture, not a remote downloader fallback.

## Historical pre-beta gates and remaining stable-release work

1. **Replaced for beta:** original native development inputs were Maven
   `io.github.junkfood02.youtubedl-android` 0.18.1 AARs. No Java downloader wrapper
   is linked, only packaged runtime payloads. These are not the audited Windows
   LGPL bundle. FFmpeg contains `--enable-gpl` and `--enable-version3`. Pin exact
   corresponding source/build recipes/notices for all dependencies or replace
   with a reproducible build. Beta 1 uses the replacement and publishes the
   matching sources/notices; the complete bundle is not MIT-only.
2. Live authorized-source tests for six providers, YouTube playlist quality,
   current EJS/TLS behavior and extractor child-process cancellation.
3. Physical ARM64 device, Android 8+ matrix, 16 KB page-size compatibility,
   denied/revoked permissions, full/offline storage and concurrent exports.
4. Broader process-death and long background/timeout/rotation/keyboard checks.
   Android now persists queue/history privately and restores unfinished jobs
   paused, never auto-started. Interrupted exports require folder review to avoid
   duplicates. Corrupt/unsupported journals are preserved and block new work.
   History pruning/recovery UI and crash injection at every export boundary remain
   pending. Current journal limit is 2,000 jobs / 16 MiB.
   Completed SAF files survive app removal; unfinished private files do not.
5. Release signing, acceptance, third-party sources, checksums and honest notes
   are included with beta 1. The maintainer still needs a portable secure backup
   of the permanent key/password. No stable mobile v1 is claimed.

The source audit and reproducible inventory procedure are tracked in
[packaging/android/README.md](../packaging/android/README.md). The inventory does
not certify corresponding sources or license completeness.

## iOS: on hold (maintainer decision)

Do not start iOS implementation as part of the current Android completion task.
No iOS host, IPA or installation profile exists in this change. The maintainer
has an iPhone but no Mac. A macOS/Xcode build environment, potentially a suitable
GitHub macOS runner, is required; no such job has been configured or run.

The desktop/Android subprocess backend cannot simply go into an IPA. iOS needs
in-process extraction/JavaScript and signed native FFmpeg libraries, Files/
security-scoped storage, background transfer/suspension handling, a native host
and physical iPhone acceptance. Shared Core/Presentation is ready for adapters
but does not implement them. No undisclosed remote server or lite substitute
is introduced.

Distribution needs a documented signing/sideloading route; a generic
`.mobileconfig` does not install this arbitrary native app. Do not request Apple
passwords/signing keys in chat or modify the private ruzgarefe.com repository.

## References

- [Avalonia shared architecture](https://docs.avaloniaui.net/docs/app-development/cross-platform-solution-setup)
- [Android folder access](https://developer.android.com/training/data-storage/shared/documents-files)
- [Android foreground services](https://developer.android.com/develop/background-work/services/fgs/service-types)
- [Android background timeouts](https://developer.android.com/develop/background-work/services/fgs/timeout)
- [.NET Process.Start platforms](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start?view=net-10.0)
- [Python on iOS](https://docs.python.org/3/using/ios.html)
- [Apple ad hoc signing](https://developer.apple.com/help/account/provisioning-profiles/create-an-ad-hoc-provisioning-profile)
