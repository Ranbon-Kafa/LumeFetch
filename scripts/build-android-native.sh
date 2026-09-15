#!/usr/bin/env bash
# Called by Build-AndroidNative.ps1. Build only from verified source snapshots.
set -euo pipefail
export PATH=/mingw64/bin:/usr/bin
repo="$(cygpath -u "$LUMEFETCH_BUILD_REPO")"
recipe="$(cd "$(dirname "$0")" && pwd)"
work="$(cygpath -u "$LUMEFETCH_NATIVE_WORK")"
ndk="$(cygpath -u "$LUMEFETCH_ANDROID_NDK")"
target="${LUMEFETCH_ANDROID_TARGET:?}"
jobs="${LUMEFETCH_BUILD_JOBS:-4}"
tools="$ndk/toolchains/llvm/prebuilt/windows-x86_64/bin"
export LUMEFETCH_NDK_CLANG="$tools/clang.exe"
prefix="$work/$target/prefix"
python="$work/python-$target/prefix"
build="$work/$target/build"
mkdir -p "$prefix" "$build" "$work/host"
export CC="$recipe/android-clang.sh"
export AR="$tools/llvm-ar.exe"
export RANLIB="$tools/llvm-ranlib.exe"
export NM="$tools/llvm-nm.exe"
export STRIP="$tools/llvm-strip.exe"
export CFLAGS='-O2 -fPIC -std=gnu17'
export LDFLAGS='-Wl,-z,max-page-size=16384,-z,common-page-size=16384'
unset PKG_CONFIG_PATH
export PKG_CONFIG_LIBDIR="$prefix/lib/pkgconfig"
export PKG_CONFIG="pkg-config --define-prefix"

cd "$build"
for archive in ffmpeg-9.0.1.tar.xz lame-3.100.tar.gz opus-1.6.1.tar.gz zlib-1.3.2.tar.gz; do
    folder="${archive%.tar.*}"
    if [[ ! -d "$folder" ]]; then tar -xf "$repo/.tools/release-sources/$archive"; fi
done
if [[ ! -f "$prefix/lib/libz.a" ]]; then
    cd "$build/zlib-1.3.2"
    CHOST="$target" ./configure --prefix="$prefix" --static
    make -j"$jobs"
    make install
fi
if [[ ! -f "$prefix/lib/libmp3lame.a" ]]; then
    cd "$build/lame-3.100"
    ./configure --host="$target" --build=x86_64-w64-mingw32 --prefix="$prefix" \
        --disable-shared --enable-static --with-pic --disable-frontend --disable-decoder --disable-nasm
    make -j"$jobs"
    make install
fi
if [[ ! -f "$prefix/lib/libopus.a" ]]; then
    cd "$build/opus-1.6.1"
    ./configure --host="$target" --build=x86_64-w64-mingw32 --prefix="$prefix" \
        --disable-shared --enable-static --with-pic --disable-doc --disable-extra-programs
    make -j"$jobs"
    make install
fi

# Python.org's Android distribution includes versioned OpenSSL libraries.
# OpenSSL 3 is Apache-2.0; enabling it in FFmpeg selects LGPLv3, never GPL/nonfree.
cd "$build/ffmpeg-9.0.1"
arch=x86_64
if [[ "$target" == aarch64-linux-android ]]; then arch=aarch64; fi
if [[ "${LUMEFETCH_RESUME_FFMPEG:-0}" != 1 ]]; then
./configure --prefix="$prefix" --target-os=android --arch="$arch" --enable-cross-compile \
    --pkg-config=pkg-config --pkg-config-flags='--static --define-prefix' \
    --cc="$CC" --ar="$AR" --ranlib="$RANLIB" --nm="$NM" --strip="$STRIP" \
    --host-cc=gcc --disable-autodetect --disable-static --enable-shared \
    --disable-doc --disable-debug --disable-ffplay --disable-x86asm \
    --enable-pthreads --enable-libmp3lame --enable-libopus --enable-zlib \
    --enable-openssl --enable-version3 --disable-gpl --disable-nonfree \
    --extra-cflags="-I$prefix/include -I$python/include -fPIC" \
    --extra-ldflags="-L$prefix/lib -L$python/lib -Wl,-z,max-page-size=16384,-z,common-page-size=16384" \
    --extra-version=lumefetch-android1
else
    # Explicit recovery only; config.mak records the actual previous arguments.
    test -f ffbuild/config.mak
    grep -q '^#define CONFIG_GPL 0$' config.h
    grep -q '^#define CONFIG_NONFREE 0$' config.h
    grep -q '^#define CONFIG_LIBMP3LAME 1$' config.h
    grep -q '^#define CONFIG_LIBOPUS 1$' config.h
    grep -q '^#define CONFIG_OPENSSL 1$' config.h
fi
make -j"$jobs"
make install

# Generate the QuickJS REPL bytecode using a same-version native host compiler.
if [[ ! -d "$work/host/quickjs-2026-06-04" ]]; then
    tar -xf "$repo/.tools/android-native/downloads/quickjs-2026-06-04.tar.xz" -C "$work/host"
fi
cd "$work/host/quickjs-2026-06-04"
if [[ ! -f repl.c ]]; then
    make -j"$jobs" qjsc.exe CONFIG_WIN32=y CROSS_PREFIX= CC=gcc AR=ar
    ./qjsc.exe -s -c -o repl.c -m repl.js
fi
"$CC" -O2 -fPIE -pie -fwrapv -D_GNU_SOURCE '-DCONFIG_VERSION="2026-06-04"' \
    qjs.c repl.c quickjs.c dtoa.c libregexp.c libunicode.c cutils.c quickjs-libc.c \
    -lm -ldl -pthread -Wl,-z,max-page-size=16384,-z,common-page-size=16384 -o "$prefix/bin/qjs"
"$CC" -O2 -fPIE -pie -I"$python/include/python3.14" \
    "$recipe/python-launcher.c" -L"$python/lib" -lpython3.14 \
    -Wl,-z,max-page-size=16384,-z,common-page-size=16384 -o "$prefix/bin/python-launcher"
"$STRIP" "$prefix/bin/qjs" "$prefix/bin/python-launcher"
mkdir -p "$prefix/build-record"
cp "$build/ffmpeg-9.0.1/config.h" "$build/ffmpeg-9.0.1/ffbuild/config.mak" "$prefix/build-record/"
"$LUMEFETCH_NDK_CLANG" --version > "$prefix/build-record/compiler.txt"
cp "$recipe/build-android-native.sh" "$recipe/android-clang.sh" "$recipe/python-launcher.c" "$prefix/build-record/"
printf 'Built native Android tools for %s\n' "$target"
