# LumeFetch Android — bundled software and corresponding source

LumeFetch's code is MIT licensed, copyright Rüzgar Efe (ruzgarefe.com).
The APK is NOT MIT-only. Included programs keep their own licenses and copyright
notices. This notice collection is not legal certification or permission to
download anyone else's content. Spotify is disabled.

## Bundled runtime

- FFmpeg/FFprobe 9.0.1: LGPL-3.0-or-later in this Android configuration
  (`--enable-version3 --enable-openssl`, GPL/nonfree disabled), shared libraries.
- LAME 3.100: LGPL-2.0-or-later; Opus 1.6.1: BSD; zlib 1.3.2: zlib license.
- Official CPython 3.14.7: PSF license and included historical/third-party notices.
  Android dependencies: OpenSSL 3.5.7 (Apache-2.0), SQLite 3.50.4 (public domain),
  bzip2 1.0.8 (bzip2 license), libffi 3.4.4 (MIT), liblzma from xz 5.4.6
  (public-domain core, as documented upstream), zstd 1.5.7 (BSD option).
- QuickJS 2026-06-04: MIT. yt-dlp 2026.08.19: Unlicense; its EJS 0.8.0 component
  includes astring 1.9.0 and meriyah 6.1.4 under their MIT licenses.
- Mutagen 1.47.0: GPL-2.0-or-later in the separate Python/downloader process;
  certifi 2026.7.22 and the CA bundle: MPL-2.0.
- .NET/Mono 10.0.11, .NET for Android 36.1.2, Avalonia 12.1.2 and MicroCom:
  MIT and their third-party notices; Inter: SIL Open Font License 1.1.
  SkiaSharp 3.119.4 and HarfBuzzSharp 8.3.1.3 retain their native graphics/font
  dependency notices. AndroidX, Kotlin and associated Java bindings retain
  package-provided MIT/Apache-2.0 notices and upstream copyright statements.
- NDK r30 runtime/compiler notices are included as a toolchain-wide superset.
  Android OS libraries are provided by the device, not redistributed in our APK.

`inventory.json` records verified source archive hashes, exact NuGet versions,
repository commits, licenses and public notice origins. The source notice
collection preserves upstream subproject licenses even where only the parent
library is used. Likewise the restore graph includes build-time and other-OS
packages: neither is a claim that every listed subcomponent is in the APK.

## Get source / rebuild / replace

The matching `LumeFetch-Android-1.1.0-beta.1-corresponding-sources.zip` and
`LumeFetch-Android-1.1.0-beta.1-notices.zip` accompany the APK at:

https://github.com/Ranbon-Kafa/LumeFetch/releases/tag/android-v1.1.0-beta.1

Download source without a fee or additional permission. The source package
contains the exact native/Python dependency archives, CPython Android dependency
patches/recipes, LumeFetch source and build scripts, and the actual native build
configuration (local build paths normalized). No FFmpeg, LAME, Opus or zlib source
patches are applied by LumeFetch. See `NATIVE-BUILD.md` and the lock files in the
source tree. The official CPython Android distribution's matching source and
dependency recipes are included; Python's Android build instructions are in
its `Android/README.md`. Build-tool downloads remain pinned in our scripts.

To use modified LGPL libraries, rebuild the shared libraries, package them with
`Package-AndroidNativeTools.ps1` (which regenerates the integrity manifest), build
the APK, and sign your modified APK with your own key. No private LumeFetch key is
required. Android will not install a differently signed APK over the official
app: choose a separate application ID or explicitly back up data before changing
installations. LumeFetch does not prohibit modification, relinking or reverse
engineering needed to debug your modifications. Runtime integrity checks use
the manifest supplied by your own build, not a hardcoded publisher signature.

The private signing key is not part of corresponding source and is never shared.
The public certificate fingerprint is in `SIGNING.md`. There is no DRM/access
control bypass. Tools are already inside the APK; network access is needed for
media, not first-launch tool installation.
