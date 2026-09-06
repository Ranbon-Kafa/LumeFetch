#!/usr/bin/env bash
# Run through Build-FFmpeg.ps1. No system installs and no automatic source edits.
set -euo pipefail
export PATH=/mingw64/bin:/usr/bin
repo="$PWD"
work="$(cygpath -u "$LUMEFETCH_NATIVE_WORK")"
prefix="$work/prefix"
sources="$repo/.tools/release-sources"
jobs="${LUMEFETCH_BUILD_JOBS:-4}"
mkdir -p "$work" "$prefix"
cd "$work"
for archive in ffmpeg-9.0.1.tar.xz lame-3.100.tar.gz opus-1.6.1.tar.gz zlib-1.3.2.tar.gz; do
    folder="${archive%.tar.*}"
    if [[ ! -d "$folder" ]]; then tar -xf "$sources/$archive"; fi
done
export CFLAGS='-O2 -std=gnu17'
export LDFLAGS='-static-libgcc'
if [[ ! -f "$prefix/lib/libz.a" ]]; then
    cd "$work/zlib-1.3.2"
    make -f win32/Makefile.gcc -j"$jobs" libz.a
    mkdir -p "$prefix/include" "$prefix/lib"
    cp libz.a "$prefix/lib/"
    cp zlib.h zconf.h "$prefix/include/"
fi
if [[ ! -f "$prefix/lib/libmp3lame.a" ]]; then
    cd "$work/lame-3.100"
    ./configure --host=x86_64-w64-mingw32 --prefix="$prefix" --disable-shared --enable-static --disable-frontend --disable-decoder --disable-nasm
    make -j"$jobs"
    make install
fi
if [[ ! -f "$prefix/lib/libopus.a" ]]; then
    cd "$work/opus-1.6.1"
    ./configure --host=x86_64-w64-mingw32 --prefix="$prefix" --disable-shared --enable-static --disable-doc --disable-extra-programs
    make -j"$jobs"
    make install
fi
cd "$work/ffmpeg-9.0.1"
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
unset CFLAGS
./configure --prefix="$prefix" --target-os=mingw32 --arch=x86_64 \
    --disable-autodetect --disable-static --enable-shared --disable-doc \
    --disable-debug --disable-ffplay --disable-x86asm --enable-w32threads \
    --enable-schannel --enable-libmp3lame --enable-libopus --enable-zlib \
    --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib -static-libgcc" \
    --extra-version=lumefetch1
make -j"$jobs"
make install
target="$repo/.tools/ffmpeg-lumefetch"
mkdir -p "$target"
cp "$prefix/bin/ffmpeg.exe" "$prefix/bin/ffprobe.exe" "$prefix/bin/"*.dll "$target/"
cp COPYING.LGPLv2.1 "$target/LICENSE.txt"
# GCC's Windows runtime dependency must not be satisfied accidentally by PATH.
cp /mingw64/bin/libwinpthread-1.dll "$target/"
licenses="$repo/.tools/licenses/native-build"
mkdir -p "$licenses"
for component in gcc-libs crt libwinpthread; do
    cp -R "/mingw64/share/licenses/$component" "$licenses/"
done
"$target/ffmpeg.exe" -hide_banner -buildconf
"$target/ffmpeg.exe" -hide_banner -version
