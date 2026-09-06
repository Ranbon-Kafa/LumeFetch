# Third-party notices

LumeFetch's own code is MIT-licensed. **Bundled tools and their components retain
their separate licenses; the complete Windows package is not MIT-only.**
Binary identities are pinned in `packaging/windows/dependencies.json`.
Exact source origins and hashes are in `packaging/windows/sources.json` and the
generated `licenses/source-inventory.json`; binary hashes are recorded in
`licenses/tool-binaries.json`.

## FFmpeg and audio libraries

LumeFetch builds FFmpeg 9.0.1 from unmodified source with shared DLLs, without
`--enable-gpl` or `--enable-nonfree`. It uses LGPL-2.1-or-later FFmpeg and LAME
3.100, BSD-licensed Opus 1.6.1 and zlib 1.3.2. GCC runtime exception and MinGW/
libwinpthread notices accompany the runtime components. The configuration and
build recipe are included; users may rebuild or replace tools and DLLs.

The matching **corresponding-sources ZIP must accompany the installer/portable ZIP**
on [GitHub Releases](https://github.com/Ranbon-Kafa/LumeFetch/releases), including
all four exact source archives and build instructions. There are no source
patches in this FFmpeg build. Read [rebuild instructions](docs/CORRESPONDING-SOURCES.md)
and [FFmpeg's redistribution guidance](https://ffmpeg.org/legal.html).

## yt-dlp

The unmodified official Windows yt-dlp 2026.08.19 executable is a separate process.
Its standalone Python source is Unlicense, but the executable bundles components
under other licenses, including GPL-licensed Mutagen. See the upstream
`yt-dlp-THIRD_PARTY_LICENSES.txt`, exact Python package source archives and collected
notices. The CPython source and curl-impersonate Windows build recipes/native
sources are also preserved. Upstream's notice aggregate covers multiple operating
systems; its inclusion does not imply every mentioned library is in the Windows exe.

- [yt-dlp source and license](https://github.com/yt-dlp/yt-dlp/tree/2026.08.19)
- [Official release](https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19)

## Deno

Deno 2.9.6 supplies yt-dlp's JavaScript runtime. Its MIT license, V8/ICU and Rust
dependency notices/sources are included. The Cargo.lock inventory is a **superset**
that includes build, test and non-Windows dependencies; it is not a binary SBOM.

Some crates omit a license file. The inventory distinguishes recovered workspace
notices from original metadata/SPDX declarations paired with standard license
terms. Historical-commit retrieval failures are recorded, not hidden. Full
unmodified source archives preserve all upstream statements. This collection
is not a legal certification.

- [Deno source](https://github.com/denoland/deno/tree/v2.9.6)
- [Deno license](https://github.com/denoland/deno/blob/v2.9.6/LICENSE.md)

## Desktop runtime and fonts

Self-contained packages include .NET, Avalonia, SkiaSharp, HarfBuzzSharp and their
native graphics/font dependencies. Package-provided LICENSE/NOTICE files are in
`licenses/`; exact NuGet versions are in `licenses/nuget-dependencies.json`.
Avalonia and .NET use MIT terms; Inter is distributed under SIL Open Font License
1.1, whose complete text is included. Native Skia/HarfBuzz notices preserve their
third-party attributions. Inno Setup's own compiler licensing applies to its use.

Spotify is disabled in v1 pending service-policy review. No service endorsement
or permission to download media is granted by LumeFetch. Content licenses,
service terms and applicable law remain the user's and distributor's responsibility.
