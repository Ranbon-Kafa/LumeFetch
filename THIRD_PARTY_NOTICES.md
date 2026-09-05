# Third-party notices

LumeFetch integrates with independent projects that are not covered by its MIT
license. Windows preview packages bundle the versions pinned in
`packaging/windows/dependencies.json`; their original notices are placed in
`licenses/` and `tools/ffmpeg/LICENSE.txt`. NuGet dependencies are listed in
`licenses/nuget-dependencies.json`.

## Avalonia

Avalonia UI is distributed under the MIT License.

- Project: https://github.com/AvaloniaUI/Avalonia
- License: https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md

## FFmpeg

FFmpeg is licensed under LGPL 2.1 or later, with optional components that can
make a particular build GPL-licensed. Distributors must review the exact build
they package and comply with its corresponding license obligations.

The current Windows preview uses BtbN's shared LGPLv3 build
`n9.0.1-26-g5c8e7e2433`, not a GPL/nonfree variant. LumeFetch invokes its separate
process; users can replace the tools and DLLs. Before public redistribution,
mirror the exact corresponding sources, dependency sources, patches and build
configuration beside the downloadable binaries. Local preview packaging does
not complete that audit; see `docs/RELEASE-CHECKLIST.md`.

- Project: https://ffmpeg.org/
- Legal information: https://ffmpeg.org/legal.html

## yt-dlp

yt-dlp is an independent project. Its standalone binaries include components
under multiple licenses; consult the notices shipped with the selected release.

- Project: https://github.com/yt-dlp/yt-dlp
- License: https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE
- Release files: https://github.com/yt-dlp/yt-dlp/releases

## Deno

Deno 2.9.6 supplies the JavaScript runtime used by the YouTube extractor. Deno is
MIT-licensed and includes independently licensed components, including V8.
Its upstream license is copied into the Windows package.

- Project: https://github.com/denoland/deno
- License: https://github.com/denoland/deno/blob/v2.9.6/LICENSE.md

## .NET, SkiaSharp, HarfBuzzSharp and Inter

Self-contained builds include Microsoft's .NET runtime and Avalonia's native
graphics/font dependencies. Package-provided license/notice files and a NuGet
inventory are copied into `licenses/`. Inter's font license and native runtime
notices must also be included in the public-release redistribution audit.
Inno Setup is a build-time tool, not installed with LumeFetch; its own licensing
conditions apply to use of the compiler.

LumeFetch does not grant permission to download third-party content. Users and
distributors remain responsible for applicable licenses, service terms, and
local law.
