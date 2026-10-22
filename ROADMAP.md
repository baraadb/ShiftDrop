# Roadmap

Planned work, newest requests first. Items move to CHANGELOG.md when they ship.

## Next release

- **Update download progress in Settings.** When an update is being downloaded (from Settings → Updates or from the update card), show a progress bar in the Settings window with how much has arrived (percentage and MB of MB), instead of only "Downloading…". `Updater.InstallAsync` already reports progress through `IProgress<double>`; wire it to a progress bar in `SettingsWindow` (and to the card in the corner).

## Later

- Optional Office pack (LibreOffice, downloaded from Settings): PowerPoint → PDF/pictures, legacy DOC/XLS/PPT, Word → PDF with the exact layout.
- A keyboard shortcut that opens the wheel for the files selected in File Explorer (Windows 11 tabs make finding the active selection tricky).
- Settings window in the new design.
- HEIC output.
- New demo video/GIF and Store screenshots with the 0.4 wheel.
