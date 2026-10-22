# Changelog

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
