# Roadmap

Planned work, newest requests first. Items move to CHANGELOG.md when they ship.

## Next release

- Store names (reserved 2026-10-23, must be used in a submission within three months): English **ShiftDrop - File Converter**, Arabic **ShiftDrop - محول الملفات**. The MSIX needs both, as a localized `Properties/DisplayName` (resources.pri with en-US and ar strings). The MSIX `Properties/DisplayName` must match the name reserved in Partner Center (Manage app names); keep the Start menu and tray name (`VisualElements DisplayName`) as plain "ShiftDrop". Then pick the new name under Store listings > Product name.

## Later

- Optional Office pack (LibreOffice, downloaded from Settings): PowerPoint → PDF/pictures, legacy DOC/XLS/PPT, Word → PDF with the exact layout.
- A keyboard shortcut that opens the wheel for the files selected in File Explorer (Windows 11 tabs make finding the active selection tricky).
- Settings window in the new design.
- HEIC output.
- Background removal again, rebuilt: it was taken out in 0.6.0 because the results were not good enough (a better model, cleaner edges, and a lighter download).
- New demo video/GIF and Store screenshots with the 0.4 wheel.
