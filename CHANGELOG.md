# Changelog

## 0.6.1

- **Microsoft Store:** the app is listed as "ShiftDrop - File Converter" (in Arabic: "ShiftDrop - محول الملفات"). On your PC it is still called ShiftDrop.
- Release pages link to ShiftDrop on the Microsoft Store.

## 0.6.0

**A much smaller ShiftDrop, with its own FFmpeg.**

- **Our own FFmpeg.** ShiftDrop now ships an FFmpeg built just for it: every format FFmpeg knows, but only the outside libraries ShiftDrop actually uses, compiled into shared DLLs so `ffmpeg.exe` and `ffprobe.exe` no longer carry a full copy each. FFmpeg went from 264 MB to 64 MB on disk, and from 178 MB to 24 MB to download.
- **Faster.** Measured on the same machine against the FFmpeg used before: FFmpeg starts about 20–30% faster (every conversion, thumbnail and editor preview starts it), reading a file's details is about 17% faster, and the whole set of test conversions took 3.5% less time. FFmpeg is compiled with GCC 16 and takes AV1 and MP3 from their newest code. Each new FFmpeg build is tested and timed before it is published, and a slower build never ships.
- **SVG pictures** are drawn by resvg now (sharper text and gradients). Small icons are drawn at least 1024 pixels wide, so the result is useful.
- **Background removal is gone for now.** Its results were not good enough; it will come back, rebuilt, in a later version. Updating removes its files (about 55 MB).
- The FFmpeg build is open: `tools/ffmpeg/build.sh` with pinned versions, rebuilt monthly with FFmpeg's newest code once it passes the same checks.

## 0.5.1

- **Play selection works**: the trim editor and the audio channels editor now play the sound. Windows was refusing the preview because its size was missing.
- **The video moves while it plays**: in the video trim editor, the picture follows the playhead, and goes back to the chosen end when playback stops.
- Video frames in the editors load faster (the video size is read once).

## 0.5.0

- **Trim on a timeline.** Trimming audio or video now opens an editor: the waveform (and, for videos, a strip of frames and a big picture of the frame at the end you're moving). Drag the two ends or the whole selection, play the selected part, or let ShiftDrop cut the quiet ends for you. Arrow keys nudge an end, Tab switches ends, Space plays.
- **Crop videos.** A new video tool: drag the frame or its corners, pick a ratio (Free, 1:1, 4:3, 3:2, 16:9, 9:16), and slide through the video to see any moment.
- **Picture crop** uses the same new editor.
- **Audio channels.** Mono or stereo, with a level for the left and right sides (0–200%), shown on each side's waveform, and a preview of the result. It replaces the simple "Mono" tool.
- All editors follow the app's light or dark look and work in Arabic.

**محرر قص على شريط زمني للصوت والفيديو، قص إطار الفيديو، ونافذة قنوات الصوت.**

## 0.4.2

- Fixed: Settings didn't open in 0.4.1 (from the tray menu or by clicking the icon).

**إصلاح: نافذة الإعدادات لم تكن تفتح في 0.4.1.**

## 0.4.1

- **The wheel opens smoothly again.** The picture in the middle was being requested from Windows on every frame while it loaded; now it's asked for once. The still parts of the wheel, the labels and the middle are drawn once and reused, and the opening animation is shorter (each frame takes about a third less work, much less when nothing changes).
- **Update download progress.** While an update downloads, Settings → Updates shows a progress bar with the percentage and megabytes, and so does a card in the corner.
- **Lighter in the background.** Background removal loads only when you use it and lets go of its memory after two minutes; ShiftDrop hands unused memory back to Windows after start-up and after each job.

**العجلة صارت تفتح بسلاسة، وشريط تقدّم لتنزيل التحديث في الإعدادات، واستهلاك أقل للذاكرة.**

## 0.4.0

- **A new look for the wheel.** Icons for every tool and action, a colour for every kind of file, a glassy plate, smooth opening and hover animations, a preview of the dragged file in the middle, and a strip under the wheel showing the three wheels and their keys. Light and dark.
- **Spreadsheets without Office:** XLSX, ODS, CSV and TSV convert into each other, and to PDF (a proper table, Arabic included) and HTML. Old Arabic CSV files open correctly.
- **PDF to Excel:** rebuilds the tables of invoices, statements and reports into an XLSX sheet.
- **PDF to PowerPoint:** one sharp slide per page. **Pictures to slides:** a PPTX from the images you drop.
- **EPUB:** documents and PDFs to e-books, and e-books back to Word, PDF, Markdown or text.
- **More documents in:** RTF and HTML files now convert like Word, Markdown and text.
- **Tables are kept** when Word, Markdown, HTML and spreadsheets move between formats (they used to become plain lines).
- **Split** videos and audio into parts of any length (no re-encoding).
- **Float:** a new action that keeps files in a small card on top of everything; drag the card into a mail, a chat or a folder later.
- Progress cards show the operation's colour and icon.

**تصميم جديد للعجلة، جداول Excel بدون Office، من PDF إلى Excel وPowerPoint، كتب EPUB، تقسيم الفيديو والصوت، وإجراء «إبقاء عائمًا».**

## 0.3.0

- **Crop pictures.** A small editor opens on the picture: drag the frame or its corners, pick a ratio (Free, 1:1, 4:3, 3:2, 16:9, 9:16), press Enter.
- **Remove background.** Finds the subject and saves a transparent PNG, on your PC (ONNX Runtime with the U²-Net "silueta" model).
- **Share.** A new action opens the Windows share sheet (Nearby sharing, Mail, Teams, WhatsApp…) for the dropped files.
- **Support the developer.** ShiftDrop stays free; an optional PayPal link is in Settings → About and in the tray menu.
- New README with a demo, screenshots and an Arabic section.
- Background work now always reports back on the UI thread (steadier update downloads and cards).

**قص الصورة، إزالة الخلفية، المشاركة، وزر دعم المطوّر.**

## 0.2.0

- One-file installer (`ShiftDrop-Setup-<version>.exe`) in English and Arabic, per-user, no admin rights.
- Settings window from the tray icon: start with Windows, language, look, keys, wheel size, where results go, updates and more.
- Automatic updates from GitHub releases.
- New icon.

## 0.1.0

- First version: the Shift wheel for images, video, audio, PDF, documents, subtitles and archives, with tools (Ctrl) and actions (Alt).
