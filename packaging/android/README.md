# Android release packaging

Current: **1.1.0-beta.1 / code 10**, ARM64 APK. The original Maven runtime below
is NOT used in the release. Official CPython and source-built FFmpeg/QuickJS,
private runtime library names, transitive notices and corresponding sources
replace it. Read [native build instructions](NATIVE-BUILD.md),
[redistribution details](REDISTRIBUTION.md), [signing identity](SIGNING.md) and
[test coverage](ACCEPTANCE.md). Windows v1.0.0 is unchanged; Spotify is disabled;
iOS and the private website remain out of scope.

## Historical audit of the rejected Maven candidate (September 9)

The following audit explains why the earlier private candidate was not released.
Its distribution block applies to that candidate, not the replacement beta.

**Update 2026-09-09:** the new source-built/official-CPython candidate now passes
native emulator tests and 16-KB static checks for both ABIs. A private S25 Ultra
test APK exists; no public Android release has been made. See
[native build status](NATIVE-BUILD.md) and [acceptance record](ACCEPTANCE.md).
The AAR discussion below describes the retained legacy runtime, not the new
candidate. Its ARM64 `libsharpyuv.so` also fails 16-KB alignment.

Reviewed 2026-09-09. **Public APK distribution is not approved.** The local test
bundle works, but binary hashes are not proof of corresponding-source or license
completeness. LumeFetch's MIT license does not relicense its bundled tools.

## Pinned inputs

`tools.lock.json` records Maven AAR and official yt-dlp URLs/SHA-256. The native
payload publisher is `io.github.junkfood02.youtubedl-android` 0.18.1. Its upstream
[tag is a prerelease](https://github.com/yausername/youtubedl-android/releases/tag/0.18.1),
at [d725d5c9a18c3a99a13ee0308bf78275dc310760](https://github.com/yausername/youtubedl-android/commit/d725d5c9a18c3a99a13ee0308bf78275dc310760).
This identifies the wrapper repository, **not every native dependency's source**.
No Java/Kotlin downloader wrapper is linked into LumeFetch.

Local binary inspection found FFmpeg `--enable-gpl` and `--enable-version3`,
including codec libraries beyond the Windows LGPL build. It must not be labeled
as the audited Windows FFmpeg bundle. FFmpeg explains how optional GPL components
affect its license and why corresponding source must match the distributed
binary in its [official license guidance](https://ffmpeg.org/legal.html).

The pinned upstream [FFmpeg recipe](https://github.com/yausername/youtubedl-android/blob/d725d5c9a18c3a99a13ee0308bf78275dc310760/BUILD_FFMPEG.md)
references an unpinned Termux checkout. The [Python recipe](https://github.com/yausername/youtubedl-android/blob/d725d5c9a18c3a99a13ee0308bf78275dc310760/BUILD_PYTHON.md)
also uses an unpinned checkout and older package examples. These instructions
alone do not establish the exact sources, patches and build environment behind
the AAR's current Python, QuickJS, FFmpeg or transitive native libraries.

## Reproduce the local inventory

```powershell
./scripts/Get-AndroidTools.ps1 -Offline
./scripts/Audit-AndroidTools.ps1
```

The audit verifies prepared payload hashes and inventories ELF files, symlinks
and embedded license/metadata candidates without extracting or executing them.
It writes `artifacts/android/runtime-inventory.json`. Missing notice matches do
not prove absence of obligations; presence does not prove compliance. The report
deliberately leaves source verification and distribution approval false.

The 2026-09-09 run found 506 ELF entries and 208 symlink entries across the two
archive ABIs, in addition to eight native launchers. No notice/metadata candidates
matched the script's filename patterns. Counts are inventory entries (including
Python extension modules), not a count of independent projects or licenses.

## Required to clear the gate

1. For every shipped component, identify exact source archive/commit, patches,
   build recipe/toolchain, license and copyright notices; retain checksums.
2. Build and verify a corresponding-source archive covering all obligations,
   or replace these development inputs with reproducible, audited native builds.
3. Include notices and source access with the APK release, separate from the app
   license; review the actual resulting bundle, not only a Maven POM license.
4. Repeat device/native acceptance, signing and package verification after any
   replacement. No runtime download at first launch is introduced.

This is a release checklist, not a legal certification. Do not publish current
APKs or change the private ruzgarefe.com website as part of this audit.
