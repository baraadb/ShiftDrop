#!/usr/bin/env bash
# Builds ShiftDrop's own FFmpeg for 64-bit Windows, cross-compiled on Ubuntu with mingw-w64.
#
# What goes in: every built-in FFmpeg part (all decoders, encoders, formats and filters, so any file
# that opened before still opens), plus only the outside libraries ShiftDrop uses:
#   dav1d (fast AV1 decoding) · libaom (AVIF pictures) · SVT-AV1 (AV1 video) · libvpx (VP9/WebM)
#   libwebp · opus · vorbis · LAME (MP3) · zlib (PNG) · NVENC, AMF, Intel VPL and Media Foundation
#   (H.264 on the graphics card or through Windows).
# What stays out: ffplay, capture devices (only lavfi stays: ShiftDrop uses it to test H.264 encoders), network protocols, and the dozens of libraries
# ShiftDrop never calls. The libraries are linked into a "shared" build, so ffmpeg.exe and
# ffprobe.exe use the same DLLs instead of carrying a full copy each.
#
# Licence: everything here is LGPL 2.1+ compatible (no GPL parts), like the build used before.
#
#   sudo apt-get install mingw-w64 nasm meson ninja-build cmake pkg-config autoconf automake libtool
#   tools/ffmpeg/build.sh [work dir]        → <work dir>/out/ffmpeg-shiftdrop-win64.zip
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
source "$HERE/versions.env"
WORK="$(realpath -m "${1:-$PWD/ffbuild}")"
SRC="$WORK/src"; PREFIX="$WORK/prefix"; OUT="$WORK/out"
mkdir -p "$SRC" "$PREFIX/include" "$PREFIX/lib/pkgconfig" "$OUT"
JOBS="$(nproc)"

HOST=x86_64-w64-mingw32
# CI builds inside the toolchain image of BtbN/FFmpeg-Builds (GCC 16, the compiler of the FFmpeg
# ShiftDrop used before); Ubuntu's mingw-w64 (GCC 13, "-posix" names) works too, a little slower.
if command -v "$HOST-gcc-posix" >/dev/null; then SUFFIX=-posix; else SUFFIX=; fi
export CC="$HOST-gcc$SUFFIX" CXX="$HOST-g++$SUFFIX" AR="$HOST-ar" RANLIB="$HOST-ranlib" STRIP="$HOST-strip"
export NM="$HOST-nm" WINDRES="$HOST-windres" LD="$HOST-ld"
# -O2 is what the libraries tune for; their hot paths are hand-written assembly chosen at run time,
# so the build runs at full speed on every CPU from the last 15 years.
export CFLAGS="-O2 -pipe -I$PREFIX/include -D_FORTIFY_SOURCE=0 -fno-semantic-interposition"
export CXXFLAGS="$CFLAGS"
export LDFLAGS="-L$PREFIX/lib -static-libgcc -static-libstdc++"
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"

log() { printf '\n==> %s\n' "$*"; }

# git clone at a tag, and check the commit is the one we expect
fetch() { # name url tag commit
    local dir="$SRC/$1"
    if [ ! -d "$dir/.git" ]; then
        git -c advice.detachedHead=false clone -q --depth 1 --branch "$3" "$2" "$dir"
    fi
    local got; got="$(git -C "$dir" rev-parse HEAD)"
    [[ "$got" == "$4"* ]] || { echo "$1: expected commit $4, got $got" >&2; exit 1; }
}

# a release tarball, checked by sha256
fetch_tar() { # name url sha256
    local file="$SRC/$(basename "$2")" dir="$SRC/$1"
    [ -f "$file" ] || curl -fsSL -o "$file" "$2" || python3 -c "import sys, urllib.request; urllib.request.urlretrieve(sys.argv[1], sys.argv[2])" "$2" "$file"
    echo "$3  $file" | sha256sum -c --quiet - || { echo "$1: checksum mismatch" >&2; exit 1; }
    if [ ! -d "$dir" ]; then mkdir -p "$dir"; tar -xf "$file" -C "$dir" --strip-components=1; fi
}

cat > "$WORK/cross.meson" <<EOF
[binaries]
c = '$CC'
cpp = '$CXX'
ar = '$AR'
strip = '$STRIP'
windres = '$WINDRES'
pkg-config = 'pkg-config'
[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
EOF

cat > "$WORK/cross.cmake" <<EOF
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)
set(CMAKE_C_COMPILER $CC)
set(CMAKE_CXX_COMPILER $CXX)
set(CMAKE_RC_COMPILER $WINDRES)
set(CMAKE_ASM_NASM_COMPILER nasm)
set(CMAKE_FIND_ROOT_PATH $PREFIX $($CC -print-sysroot) /usr/$HOST)
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)
EOF
# winpthreads only as a static library: the import libraries (.dll.a) would make our DLLs need
# libwinpthread-1.dll, so their names point at the static archive (this folder is searched first)
WINPTHREAD_A="$(ls /usr/$HOST/lib/libwinpthread.a 2>/dev/null || $CC -print-file-name=libwinpthread.a)"
ln -sf "$WINPTHREAD_A" "$PREFIX/lib/libpthread.dll.a"
ln -sf "$WINPTHREAD_A" "$PREFIX/lib/libwinpthread.dll.a"

CMAKE=(cmake -G Ninja -DCMAKE_TOOLCHAIN_FILE="$WORK/cross.cmake" -DCMAKE_BUILD_TYPE=Release
       -DCMAKE_INSTALL_PREFIX="$PREFIX" -DBUILD_SHARED_LIBS=OFF -DCMAKE_POSITION_INDEPENDENT_CODE=ON)

done_mark() { [ -f "$PREFIX/.done-$1" ]; }
mark() { touch "$PREFIX/.done-$1"; }

# ---------------------------------------------------------------- libraries
if ! done_mark zlib; then
    log zlib; fetch zlib https://github.com/madler/zlib "$ZLIB_TAG" "$ZLIB_COMMIT"
    (cd "$SRC/zlib" && make -s -f win32/Makefile.gcc PREFIX="$HOST-" CC="$CC" CFLAGS="$CFLAGS" libz.a -j"$JOBS" \
        && install -D -m644 libz.a "$PREFIX/lib/libz.a" \
        && install -m644 zlib.h zconf.h "$PREFIX/include/")
    mkdir -p "$PREFIX/lib/pkgconfig"
    printf 'prefix=%s\nlibdir=${prefix}/lib\nincludedir=${prefix}/include\nName: zlib\nDescription: zlib\nVersion: %s\nLibs: -L${libdir} -lz\nCflags: -I${includedir}\n' \
        "$PREFIX" "${ZLIB_TAG#v}" > "$PREFIX/lib/pkgconfig/zlib.pc"
    mark zlib
fi

if ! done_mark dav1d; then
    log dav1d; fetch dav1d https://github.com/videolan/dav1d "$DAV1D_TAG" "$DAV1D_COMMIT"
    rm -rf "$SRC/dav1d/build"
    meson setup "$SRC/dav1d/build" "$SRC/dav1d" --cross-file "$WORK/cross.meson" --prefix "$PREFIX" --libdir lib \
        --buildtype release --default-library static -Denable_tools=false -Denable_tests=false -Denable_examples=false
    ninja -C "$SRC/dav1d/build" install >/dev/null
    mark dav1d
fi

if ! done_mark svtav1; then
    log SVT-AV1; fetch svtav1 https://github.com/AOMediaCodec/SVT-AV1 "$SVTAV1_TAG" "$SVTAV1_COMMIT"
    rm -rf "$SRC/svtav1/build"
    "${CMAKE[@]}" -S "$SRC/svtav1" -B "$SRC/svtav1/build" -DBUILD_APPS=OFF -DBUILD_DEC=OFF -DBUILD_TESTING=OFF -DENABLE_AVX512=ON >/dev/null
    ninja -C "$SRC/svtav1/build" install >/dev/null
    mark svtav1
fi

if ! done_mark aom; then
    log libaom; fetch aom "$AOM_REPO" "$AOM_TAG" "$AOM_COMMIT"
    rm -rf "$SRC/aom/out"
    "${CMAKE[@]}" -S "$SRC/aom" -B "$SRC/aom/out" -DENABLE_DOCS=OFF -DENABLE_EXAMPLES=OFF -DENABLE_TESTS=OFF \
        -DENABLE_TESTDATA=OFF -DENABLE_TOOLS=OFF -DCONFIG_AV1_DECODER=0 -DENABLE_NASM=ON >/dev/null
    ninja -C "$SRC/aom/out" install >/dev/null
    mark aom
fi

if ! done_mark vpx; then
    log libvpx; fetch vpx https://github.com/webmproject/libvpx "$VPX_TAG" "$VPX_COMMIT"
    (cd "$SRC/vpx" && make -s distclean >/dev/null 2>&1 || true
     CROSS="$HOST-" ./configure --target=x86_64-win64-gcc --prefix="$PREFIX" --enable-static --disable-shared \
        --enable-pic --enable-vp9-highbitdepth --enable-runtime-cpu-detect --disable-examples --disable-tools \
        --disable-docs --disable-unit-tests --disable-decode-perf-tests --disable-encode-perf-tests >/dev/null
     make -s -j"$JOBS" >/dev/null && make -s install >/dev/null)
    mark vpx
fi

if ! done_mark webp; then
    log libwebp; fetch webp https://github.com/webmproject/libwebp "$WEBP_TAG" "$WEBP_COMMIT"
    rm -rf "$SRC/webp/build"
    "${CMAKE[@]}" -S "$SRC/webp" -B "$SRC/webp/build" -DWEBP_BUILD_ANIM_UTILS=OFF -DWEBP_BUILD_CWEBP=OFF \
        -DWEBP_BUILD_DWEBP=OFF -DWEBP_BUILD_GIF2WEBP=OFF -DWEBP_BUILD_IMG2WEBP=OFF -DWEBP_BUILD_VWEBP=OFF \
        -DWEBP_BUILD_WEBPINFO=OFF -DWEBP_BUILD_WEBPMUX=OFF -DWEBP_BUILD_EXTRAS=OFF >/dev/null
    ninja -C "$SRC/webp/build" install >/dev/null
    mark webp
fi

if ! done_mark opus; then
    log opus; fetch opus https://github.com/xiph/opus "$OPUS_TAG" "$OPUS_COMMIT"
    rm -rf "$SRC/opus/build"
    # opus wants to download its model data in a git checkout; the release tarball has it, we turn the DNN parts off
    "${CMAKE[@]}" -S "$SRC/opus" -B "$SRC/opus/build" -DOPUS_BUILD_PROGRAMS=OFF -DOPUS_BUILD_TESTING=OFF \
        -DOPUS_DRED=OFF -DOPUS_OSCE=OFF -DOPUS_DEEP_PLC=OFF -DOPUS_FORTIFY_SOURCE=OFF -DOPUS_STACK_PROTECTOR=OFF >/dev/null
    ninja -C "$SRC/opus/build" install >/dev/null
    mark opus
fi

if ! done_mark ogg; then
    log ogg; fetch ogg https://github.com/xiph/ogg "$OGG_TAG" "$OGG_COMMIT"
    rm -rf "$SRC/ogg/build"
    "${CMAKE[@]}" -S "$SRC/ogg" -B "$SRC/ogg/build" -DINSTALL_DOCS=OFF -DBUILD_TESTING=OFF >/dev/null
    ninja -C "$SRC/ogg/build" install >/dev/null
    mark ogg
fi

if ! done_mark vorbis; then
    log vorbis; fetch vorbis https://github.com/xiph/vorbis "$VORBIS_TAG" "$VORBIS_COMMIT"
    rm -rf "$SRC/vorbis/build"
    "${CMAKE[@]}" -S "$SRC/vorbis" -B "$SRC/vorbis/build" -DCMAKE_POLICY_VERSION_MINIMUM=3.5 >/dev/null
    ninja -C "$SRC/vorbis/build" install >/dev/null
    mark vorbis
fi

if ! done_mark lame; then
    log LAME; fetch_tar lame "$LAME_URL" "$LAME_SHA256"
    (cd "$SRC/lame" && ./configure -q --host="$HOST" --prefix="$PREFIX" --enable-static --disable-shared \
        --disable-frontend --disable-decoder --disable-gtktest --enable-nasm >/dev/null \
     && make -s -j"$JOBS" >/dev/null && make -s install >/dev/null)
    mark lame
fi

if ! done_mark nvcodec; then
    log NVENC headers; fetch nvcodec https://github.com/FFmpeg/nv-codec-headers "$NVCODEC_TAG" "$NVCODEC_COMMIT"
    make -s -C "$SRC/nvcodec" PREFIX="$PREFIX" install >/dev/null
    mark nvcodec
fi

if ! done_mark amf; then
    log AMF headers
    if [ ! -d "$SRC/amf/.git" ]; then
        git -c advice.detachedHead=false clone -q --depth 1 --filter=blob:none --sparse --branch "$AMF_TAG" \
            https://github.com/GPUOpen-LibrariesAndSDKs/AMF "$SRC/amf"
        git -C "$SRC/amf" sparse-checkout set amf/public/include
    fi
    [[ "$(git -C "$SRC/amf" rev-parse HEAD)" == "$AMF_COMMIT"* ]] || { echo "AMF: unexpected commit" >&2; exit 1; }
    mkdir -p "$PREFIX/include/AMF"
    cp -r "$SRC/amf/amf/public/include/"* "$PREFIX/include/AMF/"
    mark amf
fi

if ! done_mark vpl; then
    log Intel VPL; fetch vpl https://github.com/intel/libvpl "$VPL_TAG" "$VPL_COMMIT"
    rm -rf "$SRC/vpl/build"
    "${CMAKE[@]}" -S "$SRC/vpl" -B "$SRC/vpl/build" -DBUILD_TESTS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_TOOLS=OFF \
        -DINSTALL_EXAMPLES=OFF -DINSTALL_DEV=ON >/dev/null
    ninja -C "$SRC/vpl/build" install >/dev/null
    mark vpl
fi

# ---------------------------------------------------------------- FFmpeg
# -Wl,-Bstatic: every library is taken from its .a (on Windows the system DLLs are .a import
# libraries too), so the threads and C++ runtimes end up inside our DLLs and nothing else is needed.
log FFmpeg
# FFmpeg is taken from its main branch, like the build ShiftDrop used before: a pinned commit,
# or (monthly rebuild) the newest one, which then has to pass the tests and the speed check.
rm -rf "$SRC/ffmpeg"; mkdir -p "$SRC/ffmpeg"
git -C "$SRC/ffmpeg" init -q
git -C "$SRC/ffmpeg" remote add origin https://github.com/FFmpeg/FFmpeg
if [ "${FFMPEG_FOLLOW_BRANCH:-}" = 1 ]; then
    git -C "$SRC/ffmpeg" fetch -q --depth 1 origin "$FFMPEG_BRANCH"
else
    git -C "$SRC/ffmpeg" fetch -q --depth 1 origin "$FFMPEG_COMMIT"
fi
git -C "$SRC/ffmpeg" -c advice.detachedHead=false checkout -q FETCH_HEAD
FFMPEG_COMMIT="$(git -C "$SRC/ffmpeg" rev-parse HEAD)"
FFMPEG_TAG="$FFMPEG_BRANCH $(git -C "$SRC/ffmpeg" log -1 --format=%cs)"
FF="$SRC/ffmpeg"; DEST="$WORK/ffmpeg-install"
rm -rf "$DEST"; mkdir -p "$WORK/ffmpeg-build"
cd "$WORK/ffmpeg-build"
"$FF/configure" \
    --prefix="$DEST" --target-os=mingw32 --arch=x86_64 --cross-prefix="$HOST-" --cc="$CC" --cxx="$CXX" \
    --pkg-config=pkg-config --pkg-config-flags=--static \
    --enable-shared --disable-static --disable-debug --disable-doc --enable-stripping \
    --disable-ffplay --disable-indevs --disable-outdevs --enable-indev=lavfi --disable-network --disable-autodetect \
    --enable-w32threads --enable-zlib --enable-mediafoundation --enable-d3d11va --enable-dxva2 \
    --enable-ffnvcodec --enable-nvenc --enable-nvdec --enable-cuvid --enable-amf --enable-libvpl \
    --enable-libdav1d --enable-libaom --disable-decoder=libaom_av1 --enable-libsvtav1 --enable-libvpx --enable-libwebp \
    --enable-libopus --enable-libvorbis --enable-libmp3lame \
    --extra-cflags="-I$PREFIX/include" \
    --extra-ldflags="-L$PREFIX/lib -static-libgcc -static-libstdc++ -Wl,-Bstatic" \
    --extra-libs="-lstdc++ -lwinpthread" \
    --extra-version="shiftdrop-${FFMPEG_COMMIT:0:10}" >"$WORK/configure.log" 2>&1 || { tail -40 "$WORK/configure.log"; tail -40 ffbuild/config.log; exit 1; }
make -s -j"$JOBS" >/dev/null
make -s install >/dev/null

# ---------------------------------------------------------------- package
log Package
PKG="$OUT/ffmpeg"; rm -rf "$PKG"; mkdir -p "$PKG"
cp "$DEST"/bin/ffmpeg.exe "$DEST"/bin/ffprobe.exe "$DEST"/bin/*.dll "$PKG/"
"$STRIP" --strip-unneeded "$PKG"/*.exe "$PKG"/*.dll

# every DLL a program needs must be ours or part of Windows
SYSTEM='^(kernel32|user32|gdi32|advapi32|ole32|oleaut32|shell32|shlwapi|ws2_32|bcrypt|secur32|crypt32|psapi|msvcrt|ucrtbase|api-ms-win-.*|mfplat|mfuuid|mf|strmiids|ncrypt|userenv|dbghelp|vfw32|avicap32|winmm|d3d11|dxgi|d3d12|setupapi|cfgmgr32|ntdll|uuid|dxva2)\.dll$'
missing=0
for f in "$PKG"/*.exe "$PKG"/*.dll; do
    for dep in $("$HOST-objdump" -p "$f" | awk '/DLL Name:/ {print $3}'); do
        lower="$(echo "$dep" | tr 'A-Z' 'a-z')"
        if [ -f "$PKG/$dep" ] || [[ "$lower" =~ $SYSTEM ]]; then continue; fi
        echo "!! $(basename "$f") needs $dep, which is not in the package" >&2; missing=1
    done
done
[ "$missing" = 0 ] || exit 1

{
    echo "ShiftDrop FFmpeg build"
    echo "FFmpeg $FFMPEG_TAG ($FFMPEG_COMMIT) - LGPL 2.1 or later - https://ffmpeg.org"
    echo "Compiler: $($CC --version | head -1)"
    echo "dav1d $DAV1D_TAG · libaom $AOM_TAG · SVT-AV1 $SVTAV1_TAG · libvpx $VPX_TAG · libwebp $WEBP_TAG"
    echo "opus $OPUS_TAG · ogg $OGG_TAG · vorbis $VORBIS_TAG · LAME $LAME_VERSION · zlib $ZLIB_TAG"
    echo "nv-codec-headers $NVCODEC_TAG · AMF $AMF_TAG · Intel VPL $VPL_TAG"
    echo "Built by tools/ffmpeg/build.sh: https://github.com/baraadb/ShiftDrop"
    echo
    echo "configure:"
    sed -n 's/^#define FFMPEG_CONFIGURATION "\(.*\)"/\1/p' "$WORK/ffmpeg-build/config.h"
} > "$PKG/ffmpeg-build.txt"
cp "$FF/COPYING.LGPLv2.1" "$PKG/FFMPEG-LICENSE.txt"
echo "$FFMPEG_COMMIT" > "$OUT/ffmpeg-commit.txt"

(cd "$PKG" && rm -f ../ffmpeg-shiftdrop-win64.zip && python3 -c "
import zipfile, os
with zipfile.ZipFile('../ffmpeg-shiftdrop-win64.zip', 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for f in sorted(os.listdir('.')): z.write(f)
")
du -sh "$PKG" "$OUT/ffmpeg-shiftdrop-win64.zip"
ls -l "$PKG"
