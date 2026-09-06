# Corresponding sources and build recipes

This archive accompanies LumeFetch Windows binaries on the same GitHub Release.
Keep it available with every mirror of those binaries. This is an engineering
inventory and preservation record, not a legal opinion or service endorsement.

## Contents

- `archives/`: exact, unmodified source archives and input lockfiles, allowlisted
  by `source-inventory.json`, with origin URLs and SHA-256 hashes.
- FFmpeg 9.0.1, LAME 3.100, Opus 1.6.1 and zlib 1.3.2: the exact sources used by
  LumeFetch's custom Windows FFmpeg build. No source patches are applied.
- Deno 2.9.6 and its complete registry Cargo.lock source **superset**, including
  V8/ICU and dependencies. This includes build/test/non-Windows crates and is not
  a claim that all listed code is linked into the Windows executable.
- yt-dlp 2026.08.19 and its build recipes, exact Python runtime package sources
  (including Mutagen), CPython 3.10.11 and curl-impersonate's native sources/patches.
  The Windows curl recipe disables libidn2; the upstream cross-platform notice
  aggregate contains components not present in the Windows build.
- `licenses/`: upstream notice texts, .NET/graphics/font package notices in the
  binary package, compiler runtime notices and collected source notices.

Some Rust crates omit a standalone license file. Workspace root notices are used
where their origin is known. Other MIT-declared crates preserve original package
metadata and source copyright statements together with the unchanged standard
SPDX MIT text. `review-gaps.json` explicitly records these declaration-only cases;
no copyright owners/years are fabricated. Recovered current upstream notices are
marked when the crate's historical VCS revision could no longer be retrieved.
The complete original source archives remain pinned in all cases.

## Rebuild FFmpeg

Use Windows x64, MSYS2 MinGW64 GCC, make and pkgconf. Build in an ASCII directory
without spaces. The scripts neither enable GPL/nonfree components nor alter
upstream source files. FFmpeg/ffprobe use replaceable shared FFmpeg DLLs; LAME,
Opus and zlib are linked statically into those DLLs. libgcc uses its runtime
exception and libwinpthread is distributed as a replaceable DLL with its notice.

Extract this archive. Copy `ffmpeg-9.0.1.tar.xz`, `lame-3.100.tar.gz`,
`opus-1.6.1.tar.gz` and `zlib-1.3.2.tar.gz` from `archives` into
`.tools/release-sources` below this archive's root. Then run:

```powershell
./scripts/Build-FFmpeg.ps1 -MsysRoot C:/msys64 -WorkDirectory C:/Temp/lumefetch-build
```

The script verifies all four archive hashes before extraction and builds into
`.tools/ffmpeg-lumefetch`. See `ffmpeg-buildconf.txt` for the distributed build's
configuration. Compiler/toolchain revisions may change output bytes; this recipe
does not claim bit-for-bit reproducibility. Compiler version and package notices
are recorded with release artifacts.

## Other tools

yt-dlp and Deno executables are unmodified official release assets identified by
`packaging/windows/dependencies.json`. Their source archives contain their own
build instructions and CI recipes. The original curl-impersonate archive includes
the Windows CMake recipe and patches; Python package sources include their build
metadata. NuGet dependencies are inventoried separately in the binary package.

LumeFetch's own MIT source is available at the matching release tag:
https://github.com/Ranbon-Kafa/LumeFetch. The MIT license applies to LumeFetch's
code, not to every separately licensed component shipped with it. Users may
replace tools and corresponding DLLs; there is no reverse-engineering prohibition.
