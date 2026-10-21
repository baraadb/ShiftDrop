# Changelog

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
