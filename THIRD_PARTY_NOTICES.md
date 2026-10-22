# Third-party software in ShiftDrop

ShiftDrop's own code is MIT licensed. The installer also places these programs
next to it. They run as separate programs or libraries and keep their own licences.

| Component | Used for | Licence | Source |
| --- | --- | --- | --- |
| FFmpeg and FFprobe (ShiftDrop's own LGPL build, see `tools/ffmpeg/build.sh`) | images, video, audio | LGPL 2.1 or later | https://ffmpeg.org · https://github.com/FFmpeg/FFmpeg |
| Inside that FFmpeg build: dav1d, libaom, SVT-AV1, libvpx, libwebp, Opus, Ogg, Vorbis, zlib | AV1, AVIF, VP9, WebP, Opus, Vorbis, PNG | BSD 2/3-Clause, zlib | listed with versions in `tools/ffmpeg-build.txt` |
| … LAME | MP3 | LGPL 2.0 or later | https://lame.sourceforge.io |
| … nv-codec-headers, AMF headers, Intel VPL | H.264 on NVIDIA, AMD and Intel graphics | MIT | https://github.com/FFmpeg/nv-codec-headers · https://github.com/GPUOpen-LibrariesAndSDKs/AMF · https://github.com/intel/libvpl |
| resvg | drawing SVG pictures | Apache 2.0 or MIT | https://github.com/linebender/resvg |
| PDFium (build by bblanchon) | reading, rendering, merging PDF | BSD 3-Clause / Apache 2.0 | https://github.com/bblanchon/pdfium-binaries · https://pdfium.googlesource.com/pdfium |
| .NET runtime | runs ShiftDrop | MIT | https://github.com/dotnet/runtime |
| C#/WinRT and the Windows SDK projection | the Windows share sheet | MIT | https://github.com/microsoft/CsWinRT |

FFmpeg is unmodified FFmpeg source, built by `tools/ffmpeg/build.sh` in this repository
(the script, the pinned versions in `tools/ffmpeg/versions.env` and the GitHub Actions log
let anyone rebuild it). It runs as separate programs with their own DLLs. The exact versions
and configure options are in `tools/ffmpeg-build.txt` next to the app.

Optional, not included: 7-Zip (if installed, ShiftDrop uses it to open RAR and 7z
archives), and Microsoft Print to PDF (part of Windows, used for document PDFs).
