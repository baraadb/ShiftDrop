# Third-party software in ShiftDrop

ShiftDrop's own code is MIT licensed. The installer also places these programs
next to it. They run as separate programs or libraries and keep their own licences.

| Component | Used for | Licence | Source |
| --- | --- | --- | --- |
| FFmpeg and FFprobe (LGPL build by BtbN) | images, video, audio | LGPL 2.1 or later | https://github.com/BtbN/FFmpeg-Builds · https://ffmpeg.org |
| PDFium (build by bblanchon) | reading, rendering, merging PDF | BSD 3-Clause / Apache 2.0 | https://github.com/bblanchon/pdfium-binaries · https://pdfium.googlesource.com/pdfium |
| .NET runtime | runs ShiftDrop | MIT | https://github.com/dotnet/runtime |
| C#/WinRT and the Windows SDK projection | the Windows share sheet | MIT | https://github.com/microsoft/CsWinRT |

FFmpeg is used as an unmodified separate executable. Its exact build and source
are named in `tools/ffmpeg-build.txt`, written by the installer.

Optional, not included: 7-Zip (if installed, ShiftDrop uses it to open RAR and 7z
archives), and Microsoft Print to PDF (part of Windows, used for document PDFs).
