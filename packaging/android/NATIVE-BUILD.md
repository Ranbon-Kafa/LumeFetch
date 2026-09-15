# Android native builds — 1.1.0-beta.1

The former Maven runtime is retained for comparison only. It has incomplete
source provenance and its ARM64 `libsharpyuv.so` fails the 16-KB ELF check
(alignment 4096). Do not publish that APK as the stable Android release.

The replacement uses unmodified official CPython 3.14.7 Android distributions,
our small MIT Python launcher, source-built FFmpeg 9.0.1, LAME 3.100, Opus 1.6.1,
zlib 1.3.2 and QuickJS 2026-06-04. Mutagen handles metadata/covers; certifi supplies
the CA bundle. Everything is bundled at build time, not downloaded on first run.
FFmpeg enables OpenSSL and version3, disables GPL/nonfree, so its configured
license is LGPLv3 (confirm with the resulting executable). Mutagen remains
GPL-2.0-or-later in the separate Python/extractor process. The app's MIT license
does not replace these licenses.

## Reproduction on Windows

Use an ASCII work directory without spaces and MSYS2 with make, GCC and
pkg-config. NDK r30 is verified against Google's published size/SHA-1 and a
pinned SHA-256 before extraction. Official Python archive URLs/hashes are pinned
in `python-runtime.lock.json`; the helper caches them under `.tools/android-native/downloads`.
The source hashes are in `native-sources.lock.json`; wheels/sources are in
`python-packages.lock.json`. Recipes for CPython's six Android dependencies are
pinned source archives, including their patches, not an unversioned checkout.

```powershell
./scripts/Get-AndroidNdk.ps1 -Destination <fresh-ndk-directory>
./scripts/Get-AndroidNativeSources.ps1
./scripts/Get-AndroidPythonRuntime.ps1
./scripts/Get-AndroidExtractor.ps1
./scripts/Build-AndroidNative.ps1 -Abi x86_64 -WorkDirectory <work> -NdkDirectory <ndk>/android-ndk-r30
./scripts/Package-AndroidNativeTools.ps1 -Abi x86_64 -WorkDirectory <work> -Destination <fresh-candidate>
./scripts/Test-AndroidElf.ps1 -Path <fresh-candidate> -Abi x86_64
./scripts/Build-Android.ps1 -Runtime android-x64 -BundledToolsDirectory <fresh-candidate>
./scripts/Test-AndroidPackage.ps1 -Apk <apk> -Abi x86_64 -BundledToolsDirectory <fresh-candidate>
./scripts/Test-AndroidElf.ps1 -Path <apk> -Abi x86_64
```

Use `arm64-v8a` / `android-arm64` for the phone build. Build one ABI at a time
when the shared QuickJS host generator has not yet been built. Helpers use fresh
candidate folders; the old runtime is never overwritten automatically.

The official Python archive's three known in-prefix symlinks are materialized
as copies on Windows. Runtime files retain their original bytes. `bootstrap.py`
restores stdout/stderr to private pipes because Android CPython defaults to
logcat; it also establishes the downloader's cancellation process group.

**Runtime library isolation:** do not copy the entire Python `prefix/lib/*.so*`
set into an APK. `libcrypto.so`, `libssl.so` and `libsqlite3.so*` are SDK linker
aliases; placing them on `LD_LIBRARY_PATH` caused the S25 system SQLite/OpenSSL
startup failure. Package only versioned Python and private `*_python.so` runtime
names, per [CPython's Android guide](https://docs.python.org/3.14/using/android.html).
The aliases remain in the build prefix for linking, but are excluded from
candidate 2 onward. `Test-AndroidLibraryIsolation.ps1` gates packaging, builds
and APK checks; `Test-AndroidLibraryIsolationPolicy.ps1` covers regression cases.

## Verified locally (2026-09-09)

Both ABIs built. The replacement x86_64 smoke passes FFmpeg/FFprobe, Opus,
generated HTTP bytes, queue MP3, yt-dlp analysis/post-processing, QuickJS, EJS
import, Mutagen and the TLS CA store. ARM64 Release packaging passes native
manifest/signature checks and 180 ELF 16-KB alignment checks (181 in diagnostic
build 6; 177 after alias removal in build 7). Build 6 phone diagnostics identified
system-library shadowing, fixed in build 7 packaging. Native smoke passes again
without the aliases, including Python TLS and SQLite. The maintainer confirmed
both MP3 and MP4 on S25 Ultra on September 10. The September 15 public beta
retains those native bytes and adds permanent signing and complete notice assets.
See [acceptance details](ACCEPTANCE.md).

Build scripts now run from immutable per-run snapshots. For interrupted builds,
`-ResumeConfiguredFFmpeg` explicitly reuses `ffbuild/config.mak`; it checks that
GPL/nonfree are disabled and required libraries enabled. Do not use that recovery
switch after changing sources or desired configuration. The actual config and
compiler identity are retained in `prefix/build-record`.

## Notice / source packaging

Install the .NET Android workload and restore/build the Android project to obtain
its `obj/project.assets.json`. Use the exact SDK/package versions recorded in the
release notice inventory; our toolchain is .NET 10.0.400 / Android 36.1.2 / NDK r30.
The Python notice collector requires Python 3.11+ and uses only its standard library:

```powershell
python scripts/collect-android-notices.py --assets <android-project.assets.json> --ndk <ndk-r30> --output <fresh-notices>
./scripts/Package-AndroidNativeTools.ps1 -Abi arm64-v8a -WorkDirectory <work> -Destination <fresh-runtime> -CollectedNoticesDirectory <fresh-notices>
./scripts/Package-AndroidNativeSources.ps1 -Destination <fresh-source-package>
```

Notices are embedded as `assets/notices/third-party-notices.zip`, retaining full
upstream filenames inside the ZIP without exceeding Windows aapt2 path limits.
`Add-AndroidRedistributionNotices.ps1` can alternatively add notices to an already
verified single-ABI source-built runtime without rebuilding or changing its
native/Python bytes. It rechecks each payload hash and the library isolation policy.

The release source ZIP additionally includes a `git archive` of the matching
LumeFetch source, the collected notices and `prefix/build-record` from each ABI.
Only local build paths in those records are normalized to `<WORK>`/`<REPO>`;
flags, source and compiler identities remain unchanged. Source and patch archives
are retained byte-for-byte and checked against their lock hashes. Exact
CPython dependency recipes are shipped, including their upstream patches.

## Remaining stable-release coverage

- Repeat Release UI download/MP3/playlist/cancellation/export checks on the
  replacement runtime and real authorized platform URLs.
- Beta 1 includes exact source archives, build configuration and collected
  notices. This is not a legal certification; keep them matched for every update.
- Expand testing beyond the maintainer's S25 Ultra / Android 16. Current
  x86_64 emulator uses 4-KB pages; static alignment is not a 16-KB device test.
- Keep using the permanent release identity in SIGNING.md; back it up privately.
  Local development and disposable signing-test keys are never release identities.

No iOS work or private website modifications are included.

## Provenance references

- [Official CPython 3.14.7 files](https://www.python.org/downloads/release/python-3147/)
- [CPython Android build/dependency recipe](https://github.com/python/cpython/blob/v3.14.7/Android/android.py)
- [Android dependency source/patch repository](https://github.com/beeware/cpython-android-source-deps)
- [NDK downloads/checksums](https://developer.android.com/ndk/downloads)
- [Android 16-KB alignment](https://developer.android.com/guide/practices/page-sizes)
- [FFmpeg license guidance](https://ffmpeg.org/legal.html)
- [QuickJS source](https://bellard.org/quickjs/)
