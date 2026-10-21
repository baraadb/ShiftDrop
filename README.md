# ShiftDrop

**حوّل أي ملف وأنت تسحبه.** اسحب ملفًا في مستكشف Windows واضغط **Shift**، فتظهر عجلة صيغ تحت المؤشر. أفلت الملف على الصيغة، وتنحفظ نسخة جديدة بجانب الأصل. كل شيء يعمل على جهازك، بلا رفع وبلا إنترنت.

مجاني ومفتوح المصدر بالكامل (MIT).

## التثبيت

1. فك ضغط المجلد في أي مكان.
2. انقر مرتين على **`Install.cmd`**.

السكربت يثبّت ‎.NET 10 SDK إن لم يكن موجودًا، ويبني البرنامج، وينزّل FFmpeg وPDFium، ثم يثبّته لحسابك فقط (بلا صلاحيات مدير) في `%LOCALAPPDATA%\Programs\ShiftDrop`. يضيف أيضًا اختصارًا في قائمة ابدأ وفي «إرسال إلى»، ويشغّل البرنامج مع Windows.

أول مرة تأخذ بضع دقائق، لأن FFmpeg وحده حوالي 180 ميغا.

**ملف تثبيت واحد للمشاركة:** بعد البناء، انقر مرتين على **`Build-Setup.cmd`** فيصنع `build\ShiftDrop-Setup-<الإصدار>.exe`. هذا الملف فيه كل شيء (البرنامج وبيئة .NET وFFmpeg وPDFium)، فمن يثبّته لا يحتاج إنترنت ولا .NET ولا صلاحيات مدير. للإزالة: من «التطبيقات المثبتة» في الإعدادات، أو شغّل `Uninstall-ShiftDrop.ps1`.

## الاستخدام

| الحركة | النتيجة |
| --- | --- |
| اسحب ملفًا ثم اضغط **Shift** | عجلة التحويل (الصيغ المناسبة للملف) |
| والعجلة مفتوحة اضغط **Ctrl** | عجلة الأدوات: ضغط، قص، تدوير، دمج، حذف البيانات... |
| والعجلة مفتوحة اضغط **Alt** | عجلة الإجراءات: نسخ المسار، الطرفية هنا، نسخ/نقل إلى، ZIP، الخصائص |
| أفلت على خانة | تنفيذ، ونسخة جديدة بجانب الأصل |
| أفلت في المنتصف أو خارج العجلة | إلغاء |
| زر يمين على ملف ← إرسال إلى ← ShiftDrop | العجلة بلا سحب: انقر، أو الأسهم وEnter، وTab للتبديل |

بعد ما تفتح العجلة تقدر تترك الأزرار. تعمل مع عدة ملفات مرة واحدة. والأصل لا يُمس أبدًا، ولا يُكتب فوق أي ملف.

الإعدادات من أيقونة البرنامج بجانب الساعة (زر يمين): اللغة (عربي أو إنجليزي)، مكان الحفظ، العمل في كل التطبيقات، التشغيل مع Windows، الإيقاف المؤقت.

## الصيغ

| النوع | التحويل | الأدوات |
| --- | --- | --- |
| صور (JPG, PNG, WebP, HEIC, AVIF, TIFF, BMP, ICO, SVG) | JPG, PNG, WebP, AVIF, TIFF, BMP, ICO, PDF | ضغط، نصف الحجم، تدوير، أبيض وأسود، حذف البيانات، PDF واحد من عدة صور |
| GIF | MP4, MOV, WebM, PNG, JPG, WebP | |
| فيديو (MP4, MOV, MKV, WebM, AVI, WMV...) | MP4, MOV, MKV, WebM, AVI, WMV, GIF, MP3, M4A | ضغط، قص، حذف الصوت، سرعة ×2، بطء، تدوير، لقطة، حذف البيانات، دمج |
| صوت (MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA) | كلها | ضغط، قص، توحيد الصوت، أحادي، حذف البيانات |
| PDF | JPG, PNG (300 DPI), TXT, DOCX, MD, HTML | دمج، فصل الصفحات، استخراج صفحات، تدوير |
| مستندات (DOCX, MD, TXT) | PDF, DOCX, MD, HTML, TXT | |
| ترجمات (SRT, VTT) | SRT, VTT, TXT | إزاحة التوقيت |
| أرشيف (ZIP, TAR, TAR.GZ, GZ؛ و7z/RAR مع 7-Zip) | ZIP, TAR, TAR.GZ | استخراج |

تحويل المستندات إلى PDF يتم عبر طابعة «Microsoft Print to PDF» المدمجة في Windows، فيبقى النص عربيًا صحيحًا وقابلًا للتحديد. لا يحتاج Office.

---

## English

**Convert any file while you drag it.** Drag a file in File Explorer, press **Shift**, and a wheel of formats appears under the pointer. Drop the file on a format and a new copy is saved next to the original. Everything runs on your PC.

- **Install:** unzip, double-click `Install.cmd` (per-user, no admin).
- **Share:** double-click `Build-Setup.cmd` to make `build\ShiftDrop-Setup-<version>.exe`, a single offline installer (app, .NET runtime, FFmpeg, PDFium).
- **Use:** drag + Shift for formats. While the wheel is open, Ctrl for tools and Alt for actions. Or right-click → Send to → ShiftDrop.
- **Safe:** sources are read-only, nothing is overwritten, and a failed or cancelled job leaves nothing behind.

### Build from source

```
dotnet build src/ShiftDrop                         # Windows
dotnet run --project tests/ShiftDrop.Tests          # engine tests (needs ffmpeg + pdfium on PATH or SHIFTDROP_*_DIR)
dotnet build src/ShiftDrop -p:AppTfm=net10.0        # compile check on Linux/macOS
```

| Folder | What |
| --- | --- |
| `src/ShiftDrop.Core` | Cross-platform engines: formats catalogue, job queue with atomic output, FFmpeg, PDFium, documents, subtitles, archives |
| `src/ShiftDrop` | Windows app in plain Win32: mouse hook, OLE drop-target wheel drawn with GDI+, tray, progress cards |
| `tests/ShiftDrop.Tests` | End-to-end tests that build their own fixtures |
| `installer` | Build-and-install script, uninstaller |

ShiftDrop is an independent project inspired by the drag-to-convert idea of Tangerine for Mac. It shares no code, name or assets with it.

Licence: MIT. Third-party components: see `THIRD_PARTY_NOTICES.md`.
