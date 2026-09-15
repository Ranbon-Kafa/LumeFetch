# Android acceptance record — 2026-09-15

Status: **first public Android beta**; not a stable/full-device-matrix claim.
Historical test APKs below remain debug-signed artifacts, not release downloads.

## Passed

- 110 Core/Infrastructure unit tests, zero failures (including processing preflight and native error reporting).
- Native builds: Android ARM64 and x86_64, API 26 target for our tool launchers,
  official CPython 3.14.7, FFmpeg 9.0.1, QuickJS 2026-06-04, LAME/Opus/zlib.
- SHA-pinned native/source/wheel inputs; NDK full archive matches the official
  published checksum. Source candidates retain dependency build recipes/patches.
- Both runtime candidates: 87 ELF entries pass 16-KB alignment. Final ARM64
  Release APK: 180 ELF entries pass (including .NET/graphics components).
- Emulated Android 16 x86_64: FFmpeg/FFprobe, Opus, generated HTTP download with
  identical SHA-256, queue MP3, Python/yt-dlp analysis and MP3 post-processing,
  QuickJS execution, EJS package import, Mutagen 1.47.0, certifi 2026.07.22,
  certificate verification enabled and a populated CA store.
- New runtime starts in the actual UI and previous queue history remains.
  Capture: ignored `artifacts/android/emulator/native-candidate-main.png`.
- Disposable signing test: expected identity accepted, existing output preserved,
  wrong identity rejected. No production identity was created or altered.

The latest debug smoke result is a real emulator-generated report under
`files/runtime-smoke-result.txt`, run directory
`runtime-smoke/6a87183a27634d4abe2fb38ebf0517cb`. The phone package excludes this
debug-only hook. The earlier AAR-based UI pause/resume/export/process-recovery
tests remain useful, but do not replace acceptance on this changed runtime.

## S25 Ultra test package

- Android ARM64, `com.ruzgarefe.lumefetch`, display version `1.1.0-dev`, code 3.
- Release configuration, still using the **local Android Debug certificate**.
- 65,490,338 bytes; APK v2/v3 signatures verified.
- SHA-256: `139e9d43432543e39e304d6d2940a9e3c0a214bbd07ccd899f9a7f019a859d3e`.
- File: `artifacts/android/device-test-20260909/LumeFetch-Android-1.1.0-device-test-arm64.apk`.
- The maintainer has now reported an Android 16 S25 Ultra failure by screenshot:
  FFmpeg is unavailable at startup; YouTube/Instagram MP3 and YouTube MP4 merging
  fail with yt-dlp's FFmpeg/FFprobe-not-found post-processing error. The screenshot
  shows `1.1.0-dev`, but not the installed build code. This is **failed physical
  acceptance**, not a passed download test. At this stage the native startup
  cause was unknown; build 6 subsequently exposed it (see build 7 below).
  The emulator uses 4-KB pages; no physical 16-KB success is claimed.

Copy the APK to the phone and install it for private testing. Choose a writable
subfolder using the app's folder picker, then test a source you are authorized
to download: MP4, MP3, playlist preview, pause/resume, cancellation and restart.
Check exported files in Samsung My Files. If installation is blocked, record the
exact message; do not disable all device protection or uninstall an existing
app automatically. Changing from a debug to a production certificate may require
removal of the test build, which loses app-private settings and unfinished jobs.

## Diagnostic follow-up — build 6

- ARM64 Release configuration, same app ID and local Android Debug signing
  identity, version `1.1.0-dev`, code 6; designed as an in-place test update.
- 65,510,918 bytes; SHA-256:
  `72b897cd6e15a4ceba1670837032fffd48db4d193745337d1c7f1c99a18c85a9`.
- File: `artifacts/android/device-test-20260909/LumeFetch-Android-1.1.0-diagnostics-build6-arm64.apk`.
- Both ABIs build with zero warnings/errors. ARM64 package signature, manifest,
  native payload hashes and **181** ELF 16-KB alignment checks pass.
- Settings → Tool diagnostics retains the actual startup error/exit code plus
  build, ABI, Android API and page size. FFprobe is checked as well as FFmpeg.
  Reports stay on the device; nothing is uploaded automatically.
- Processing-required yt-dlp transfers and direct MP3 conversions check tools
  before fetching media. Failures preserve their cause instead of wasting a full
  download and reporting only a generic post-processing error.
- Real Android 16 x86_64 **Release** build 6 passes both tool startup checks.
  Generated 4-second media downloaded via the UI, converted to genuine MP3 and
  exported to `Documents/fixture-8b6c39e5.mp3` (38,696 bytes, 44.1 kHz, one audio
  stream). Independent FFprobe and full FFmpeg decode pass. Output SHA-256:
  `416e5a6065f75b9bacf5bd4f187f1be868a5ba930c870c4e97334f70f0b0a461`.
  Existing history remains after the in-place emulator update.
- Headless UI tests cover the visible failure detail, recheck/recovery and
  320-pixel TR/EN layouts. These test errors are fixtures, not S25 diagnostics.
- This diagnostic-only build did not fix the startup cause. Its phone report
  identified the packaging fault below; build 6 is superseded, not stable.

## S25 library-collision fix — 2026-09-10, build 7

The maintainer's build 6 screenshot reports Android API 36, ARM64, 4,096-byte
pages and `cannot locate symbol "OpenSSL_add_all_algorithms" referenced by
"/system/lib64/libsqlite.so"`. FFmpeg exists but Android cannot link it.

Our packager incorrectly copied Python SDK linker aliases (`libcrypto.so`,
`libssl.so`, `libsqlite3.so`, `libsqlite3.so.0`) into the runtime search path.
The generic crypto alias shadows the Samsung system crypto dependency. Its
bytes are identical to `libcrypto_python.so`; its own SONAME is private, while
its public filename can still intercept the system lookup.

Build 7 packages only versioned Python and private `*_python.so` libraries,
following the [official CPython Android packaging guidance](https://docs.python.org/3.14/using/android.html).
All 87 shared objects in each build prefix were checked: none needs an excluded
alias. FFmpeg itself links `libssl_python.so` / `libcrypto_python.so`. TLS, MP3,
Opus and the bundled downloader remain enabled; no Android security controls
or system files are changed. A changed manifest selects a fresh verified runtime
cache; settings/history are not cleared and old test artifacts are retained.

- Build and APK verification now reject system-shadowing aliases in all three
  tool search directories. Thirteen synthetic policy cases pass, and the real
  failing build 6 APK is rejected. The policy tests also run in CI.
- 110 Core/Infrastructure tests pass. Both new runtime candidates contain 83
  16-KB-aligned ELF files. ARM64 Release build 7 passes signature, payload hashes,
  library isolation and 177 final-APK ELF alignment checks.
- New x86_64 native smoke passes real FFmpeg/FFprobe, Opus, SHA-verified HTTP,
  queue MP3, yt-dlp analysis and MP3 post-processing, QuickJS, Mutagen, EJS, TLS CA
  verification and SQLite. Python reports OpenSSL 3.5.7, SQLite 3.50.4 and
  `private_library_names: true`. Run: `runtime-smoke/72e2da16a1a14555919a8e484fe9a95a`.
- x86_64 **Release** build 7 also starts with FFmpeg ready and completes the
  actual UI MP3 transfer/export to `Documents/fixture-f6f611d0.mp3`. Independent
  FFprobe reports one MP3 audio stream (44.1 kHz, 4 seconds, 38,696 bytes); full
  decode succeeds. Existing history remains after the in-place update.
- Phone artifact: `artifacts/android/device-test-20260910/LumeFetch-Android-1.1.0-build7-arm64.apk`.
  ARM64 Release configuration, version `1.1.0-dev`, code 7, same local debug
  signing identity; 62,385,670 bytes. SHA-256:
  `47a4816cb0a6be6c0e7fcf89b4ec0f03ce58f03d6ebe615f0271d8772cd20c76`.
- **Maintainer confirmation, 2026-09-10:** build 7 completes both MP3 and MP4
  downloads successfully on S25 Ultra / Android 16. The confirmation does not
  specify a full live-provider matrix; no broader clearance is inferred.

## Beta release preparation — version 1.1.0-beta.1, code 10

- Temporary Settings diagnostics card removed, not merely hidden behind a toggle.
  Internal startup checks and a friendly retry banner remain; no auto-reporting.
- 110 Release unit tests, shared Release build (zero warnings/errors), 13
  library-policy tests and 320–1220px TR/EN headless UI tests pass. Empty URL
  placeholders also fit 320px, including the final beta version label.
- Maintainer-approved permanent RSA-4096 signing identity created; public
  certificate is recorded in SIGNING.md. Private key/password stay off-repo.
- Collected 294 notice/metadata files from 113 source/package/runtime inputs.
  Collection covers the restored NuGet graph and Java binding notices, native
  sources, CPython dependencies, EJS, fonts and runtime/toolchain notices.
  It is a labelled superset, not a binary-only SBOM or legal certification.

### Final beta package verification — 2026-09-15

- ARM64 and x86_64 Release builds: zero warnings/errors. Notice archive avoids
  Windows aapt2 path-length limits without dropping any of the collected texts.
- Public ARM64 APK: 62,950,227 bytes, version `1.1.0-beta.1`, code 10. SHA-256:
  `571661a4f21b09683ee3bf2bd0c8f84ee69385bf9a535c306a56f23813115a79`.
  Permanent RSA-4096 certificate matches SIGNING.md, APK v2/v3 verified;
  application is not debuggable. Runtime payload/manifest/bootstrap and embedded
  notice ZIP match their reviewed inputs; 177 ELF files pass 16-KB alignment.
- Final restored Android graph matches all 78 collected NuGet package identities.
- Clean GitHub CI exposed a format-row reflow bug when a tool failure banner
  changes the viewport. The small format list now measures every row and disables
  horizontal scrolling. Layout tests explicitly cover both tool-ready and
  tool-unavailable states at every width; clean-environment and GitHub CI pass.
- x86_64 **Release configuration** beta installs over the existing emulator test
  app without clearing settings/history. FFmpeg is ready; diagnostic settings
  card is absent. Screenshot: `docs/screenshots/lumefetch-android-settings.png`.
  This emulator artifact keeps the private test certificate for in-place testing;
  it is not a published APK or a claim of running the ARM64 release on x86_64.
- Real shared-URL analysis, MP3 queue transfer and Documents export pass on
  Android 16 emulator. Generated output is a genuine 4-second / 44.1-kHz MP3,
  38,696 bytes; independent FFprobe and complete decode pass. SHA-256:
  `416e5a6065f75b9bacf5bd4f187f1be868a5ba930c870c4e97334f70f0b0a461`.
- Native/Python bytes are identical to the respective successful build 7
  candidates. Final ARM64 signature/notices/UI changes have not themselves been
  retested on the phone; maintainer phone evidence remains build 7 MP3/MP4.

## Still required before calling Android stable

- Physical ARM64 acceptance and authorized live-source checks for the advertised
  providers; playlist quality, covers, background timeout and storage failures.
- Broader Android 8+ / vendor matrix, actual 16-KB device runs (current emulator
  and S25 are 4-KB), full/offline storage, permission revocation and long lifecycle tests.
- Maintainer-owned portable signing-key/password backup. DPAPI local storage
  does not survive migration to a different Windows account/computer by itself.

Spotify remains disabled. iOS and the private website are out of scope.
