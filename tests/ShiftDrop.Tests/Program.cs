using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using ShiftDrop.Core;
using ShiftDrop.Core.Engines;

// A self-contained test runner (no test framework needed). It builds its own fixtures with ffmpeg.
var root = Path.Combine(Path.GetTempPath(), "shiftdrop-tests-" + Guid.NewGuid().ToString("N")[..6]);
Directory.CreateDirectory(root);
Log.Folder = Path.Combine(root, "logs");
var ff = FFmpeg.Locate();
var services = new Services { FFmpeg = ff };
var queue = new JobQueue(services);
int pass = 0, fail = 0, skip = 0;
var only = args.Length > 0 ? args[0] : null;

async Task T(string name, Func<Task> body)
{
    if (only != null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
    var sw = Stopwatch.StartNew();
    try { await body(); pass++; Console.WriteLine($"  ok   {name} ({sw.ElapsedMilliseconds} ms)"); }
    catch (SkipException s) { skip++; Console.WriteLine($"  skip {name}: {s.Message}"); }
    catch (Exception e) { fail++; Console.WriteLine($"  FAIL {name}: {e.Message}"); Console.WriteLine("       " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()); }
}
void Assert(bool c, string m) { if (!c) throw new Exception(m); }
string Dir(string n) { var d = Path.Combine(root, n); Directory.CreateDirectory(d); return d; }
Operation Op(string id) => Catalog.Operations.First(o => o.Id == id);
async Task<Job> Run(string id, IReadOnlyList<string> inputs, Dictionary<string, string>? opts = null)
{
    var job = await queue.RunNowAsync(Op(id), inputs, opts);
    if (job.State != JobState.Done) throw new Exception($"{id}: {job.State} {job.Error}");
    foreach (var o in job.Outputs) Assert(File.Exists(o) || Directory.Exists(o), "missing output " + o);
    foreach (var i in inputs) Assert(!Directory.GetFileSystemEntries(Path.GetDirectoryName(i)!, ".shiftdrop-*").Any(), "staging left behind");
    return job;
}
async Task Gen(params string[] a) => await ff.RunAsync(a, 0, null, CancellationToken.None);
void NeedFf() { if (!ff.Available) throw new SkipException("ffmpeg not found"); }
void NeedPdf() { if (!Pdfium.Available) throw new SkipException("pdfium not found"); }

Console.WriteLine($"ffmpeg: {ff.FfmpegPath ?? "none"} · pdfium: {Pdfium.Available} · h264: {(ff.Available ? ff.H264 : "-")}");

// ---------- fixtures ----------
var media = Dir("media");
string png = Path.Combine(media, "photo.png"), jpg = Path.Combine(media, "photo.jpg");
string mp4 = Path.Combine(media, "clip.mp4"), mp4b = Path.Combine(media, "clip two.mp4"), wav = Path.Combine(media, "tone.wav"), gif = Path.Combine(media, "anim.gif");
// fixtures use whatever H.264 encoder this ffmpeg has (the Windows LGPL build has no libx264)
string[] H264() => ff.H264 == "libx264" ? ["-c:v", "libx264", "-preset", "ultrafast"] : ["-c:v", ff.H264];
if (ff.Available)
{
    await Gen("-f", "lavfi", "-i", "testsrc2=s=320x240:d=1", "-vf", "format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(lt(X,40),0,255)'", "-frames:v", "1", "-update", "1", png);
    await Gen("-f", "lavfi", "-i", "testsrc2=s=320x240:d=1", "-frames:v", "1", "-update", "1", "-q:v", "3", jpg);
    await Gen(["-f", "lavfi", "-i", "testsrc2=s=320x240:r=25:d=2", "-f", "lavfi", "-i", "sine=f=440:d=2", .. H264(), "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", mp4]);
    await Gen(["-f", "lavfi", "-i", "testsrc2=s=176x144:r=25:d=1", "-f", "lavfi", "-i", "sine=f=660:d=1", .. H264(), "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", mp4b]);
    await Gen("-f", "lavfi", "-i", "sine=f=440:d=2", wav);
    await Gen("-f", "lavfi", "-i", "testsrc2=s=160x120:r=10:d=1", gif);
}

Console.WriteLine("catalog");
await T("image wheel lists formats except its own", async () =>
{
    var ops = Catalog.For([png], OpGroup.Convert, services);
    var labels = string.Join(",", ops.Select(o => o.TargetExt));
    Console.WriteLine("       png → " + labels);
    Assert(!ops.Any(o => o.TargetExt == "png"), "png offered for png");
    if (ff.Available) Assert(ops.Any(o => o.TargetExt == "jpg") && ops.Any(o => o.TargetExt == "pdf"), "missing jpg/pdf");
    await Task.CompletedTask;
});
await T("video wheel and mixed batches", async () =>
{
    if (ff.Available)
    {
        var v = Catalog.For([mp4], OpGroup.Convert, services).Select(o => o.TargetExt).ToList();
        Console.WriteLine("       mp4 → " + string.Join(",", v));
        Assert(!v.Contains("mp4") && v.Contains("mov") && v.Contains("gif") && v.Contains("mp3"), "video list");
        Assert(Catalog.For([mp4, png], OpGroup.Convert, services).Count == 0, "mixed video+image should offer nothing");
        Assert(Catalog.For([png, jpg], OpGroup.Tool, services).Any(o => o.Id == "img.onepdf"), "one PDF needs 2 images");
        Assert(!Catalog.For([png], OpGroup.Tool, services).Any(o => o.Id == "img.onepdf"), "one PDF hidden for 1 image");
    }
    Assert(Catalog.For([root], OpGroup.Action, services).Any(o => o.Id == "act.zip"), "folders get actions");
    Assert(Catalog.For([root], OpGroup.Convert, services).Count == 0, "folders get no conversions");
    await Task.CompletedTask;
});
await T("max ten slots", async () =>
{
    foreach (var k in Enum.GetValues<FileKind>())
        foreach (var g in Enum.GetValues<OpGroup>())
            Assert(Catalog.For([Path.Combine(root, "x." + k)], g, services).Count <= Catalog.MaxSlots, "too many");
    await Task.CompletedTask;
});

Console.WriteLine("safety");
await T("commit never overwrites", async () =>
{
    var d = Dir("commit");
    File.WriteAllText(Path.Combine(d, "a.txt"), "original");
    var src = Path.Combine(Dir("commit-src"), "a.txt");
    File.WriteAllText(src, "new");
    var target = JobQueue.Commit(src, d);
    Assert(Path.GetFileName(target) == "a 2.txt", "got " + target);
    Assert(File.ReadAllText(Path.Combine(d, "a.txt")) == "original", "overwritten");
    await Task.CompletedTask;
});
await T("converting twice gives a numbered name", async () =>
{
    NeedFf();
    var d = Dir("twice"); var src = Path.Combine(d, "p.png"); File.Copy(png, src);
    var a = await Run("img.to.jpg", [src]); var b = await Run("img.to.jpg", [src]);
    Assert(Path.GetFileName(a.Outputs[0]) == "p.jpg" && Path.GetFileName(b.Outputs[0]) == "p 2.jpg", b.Outputs[0]);
});
await T("cancel leaves nothing behind", async () =>
{
    NeedFf();
    var d = Dir("cancel"); var src = Path.Combine(d, "long.mp4");
    await Gen(["-f", "lavfi", "-i", "testsrc2=s=640x480:r=30:d=20", .. H264(), "-pix_fmt", "yuv420p", src]);
    var job = new Job { Op = Op("vid.to.webm"), Inputs = [src] };
    var q = new JobQueue(services);
    var started = new TaskCompletionSource();
    q.Changed += j => { if (j.State == JobState.Running) started.TrySetResult(); };
    var j2 = q.Enqueue(Op("vid.to.webm"), [src]);
    q.Changed += j => { };
    await started.Task; await Task.Delay(800); j2.Cancel();
    for (int i = 0; i < 100 && !j2.Finished; i++) await Task.Delay(100);
    Assert(j2.State == JobState.Canceled, "state " + j2.State);
    await Task.Delay(300);
    Assert(Directory.GetFileSystemEntries(d).Length == 1, "left: " + string.Join(",", Directory.GetFileSystemEntries(d).Select(Path.GetFileName)));
});

Console.WriteLine("images");
foreach (var ext in new[] { "jpg", "webp", "avif", "tiff", "bmp", "ico", "pdf" })
{
    var e = ext;
    await T($"png → {e}", async () =>
    {
        NeedFf();
        if (e == "avif" && !ff.HasEncoder("libaom-av1") && !ff.HasEncoder("libsvtav1")) throw new SkipException("no AV1 encoder");
        if (e == "webp" && !ff.HasEncoder("libwebp")) throw new SkipException("no libwebp");
        if (e == "pdf") NeedPdf();
        var job = await Run($"img.to.{e}", [png]);
        var o = job.Outputs[0];
        Assert(o.EndsWith("." + e), o);
        if (e == "pdf") Assert(Pdfium.PageCount(o) == 1, "pdf pages");
        else { var info = await ff.InfoAsync(o, default); Assert(info.Width > 0, "unreadable output"); }
        if (e == "jpg")
        {
            // transparent strip must be flattened to white, not black
            var px = await ff.RunToBytesAsync(["-i", o, "-vf", "crop=1:1:5:120", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], default);
            Assert(px[0] > 230 && px[1] > 230 && px[2] > 230, $"alpha not flattened: {px[0]},{px[1]},{px[2]}");
        }
    });
}
await T("gif → png, mp4", async () => { NeedFf(); await Run("img.to.png", [gif]); var j = await Run("gif.to.mp4", [gif]); Assert((await ff.InfoAsync(j.Outputs[0], default)).HasVideo, "no video"); });
foreach (var id in new[] { "img.compress", "img.half", "img.rotate", "img.gray", "img.strip" })
{
    var i2 = id;
    await T(i2 + " (jpg, png)", async () =>
    {
        NeedFf();
        var a = await Run(i2, [jpg]); var b = await Run(i2, [png]);
        if (i2 == "img.half") { var info = await ff.InfoAsync(a.Outputs[0], default); Assert(info.Width == 160 && info.Height == 120, $"{info.Width}x{info.Height}"); }
        if (i2 == "img.rotate") { var info = await ff.InfoAsync(a.Outputs[0], default); Assert(info.Width == 240 && info.Height == 320, $"{info.Width}x{info.Height}"); }
    });
}
await T("jpeg metadata stripped losslessly", async () =>
{
    var d = Dir("exif"); var orig = File.ReadAllBytes(jpg);
    var exif = new List<byte> { 0xFF, 0xE1 }; var payload = Encoding.ASCII.GetBytes("Exif\0\0GPS-SECRET-LOCATION");
    exif.Add((byte)((payload.Length + 2) >> 8)); exif.Add((byte)((payload.Length + 2) & 0xFF)); exif.AddRange(payload);
    var withExif = orig.Take(2).Concat(exif).Concat(orig.Skip(2)).ToArray();
    var src = Path.Combine(d, "gps.jpg"); File.WriteAllBytes(src, withExif);
    var job = await Run("img.strip", [src]);
    var outBytes = File.ReadAllBytes(job.Outputs[0]);
    Assert(!Encoding.ASCII.GetString(outBytes).Contains("GPS-SECRET"), "exif kept");
    static byte[] Scan(byte[] b) { for (int i = 2; i < b.Length - 1; i++) if (b[i] == 0xFF && b[i + 1] == 0xDA) return b[i..]; return []; }
    Assert(Scan(outBytes).AsSpan().SequenceEqual(Scan(orig)) && Scan(orig).Length > 1000, "image data changed");
    await Task.CompletedTask;
});
await T("two images → one PDF", async () =>
{
    NeedFf(); NeedPdf();
    var job = await Run("img.onepdf", [png, jpg]);
    Assert(Pdfium.PageCount(job.Outputs[0]) == 2, "pages");
    var (px, w, h) = Pdfium.RenderPage(job.Outputs[0], 0, 72);
    Assert(w > 100 && h > 50, "render size");
});

Console.WriteLine("video");
foreach (var ext in new[] { "mov", "mkv", "webm", "avi", "wmv", "gif", "mp3", "m4a" })
{
    var e = ext;
    await T($"mp4 → {e}", async () =>
    {
        NeedFf();
        var job = await Run($"vid.to.{e}", [mp4]);
        var info = await ff.InfoAsync(job.Outputs[0], default);
        Assert(e is "mp3" or "m4a" ? info.HasAudio && !info.HasVideo : info.HasVideo, "streams wrong");
        Assert(info.Duration > 1.5, "duration " + info.Duration);
    });
}
await T("video tools", async () =>
{
    NeedFf();
    var trim = await Run("vid.trim", [mp4], new() { ["start"] = "0:00.5", ["end"] = "1.5" });
    var td = (await ff.InfoAsync(trim.Outputs[0], default)).Duration;
    Assert(td > 0.8 && td < 1.3, "trim duration " + td);
    var mute = await Run("vid.mute", [mp4]); Assert(!(await ff.InfoAsync(mute.Outputs[0], default)).HasAudio, "still has sound");
    var fast = await Run("vid.fast", [mp4]); var fd = (await ff.InfoAsync(fast.Outputs[0], default)).Duration; Assert(fd < 1.3, "fast " + fd);
    var slow = await Run("vid.slow", [mp4]); var sd = (await ff.InfoAsync(slow.Outputs[0], default)).Duration; Assert(sd > 3.5, "slow " + sd);
    var rot = await Run("vid.rotate", [mp4]); var ri = await ff.InfoAsync(rot.Outputs[0], default); Assert(ri.Width == 240 && ri.Height == 320, "rotate");
    await Run("vid.snapshot", [mp4]); await Run("vid.strip", [mp4]); await Run("vid.compress", [mp4]);
    var join = await Run("vid.join", [mp4, mp4b]); var jd = (await ff.InfoAsync(join.Outputs[0], default)).Duration; Assert(jd > 2.7, "join " + jd);
});
await T("bad trim range is refused", async () =>
{
    NeedFf();
    var job = await queue.RunNowAsync(Op("vid.trim"), [mp4], new Dictionary<string, string> { ["start"] = "5", ["end"] = "1" });
    Assert(job.State == JobState.Failed && job.Outputs.Count == 0, "should fail cleanly: " + job.State);
});

Console.WriteLine("audio");
foreach (var ext in new[] { "mp3", "m4a", "flac", "ogg", "opus", "aiff", "wma" })
{
    var e = ext;
    await T($"wav → {e}", async () => { NeedFf(); var j = await Run($"aud.to.{e}", [wav]); Assert((await ff.InfoAsync(j.Outputs[0], default)).HasAudio, "no audio"); });
}
await T("audio tools", async () =>
{
    NeedFf();
    foreach (var id in new[] { "aud.compress", "aud.normalize", "aud.mono", "aud.strip" }) await Run(id, [wav]);
    var t = await Run("aud.trim", [wav], new() { ["start"] = "0.5", ["end"] = "" });
    var d = (await ff.InfoAsync(t.Outputs[0], default)).Duration; Assert(d > 1.3 && d < 1.7, "trim " + d);
});

Console.WriteLine("pdf");
string textPdf = Path.Combine(media, "letter.pdf");
{
    // a hand-made 2-page PDF with real text
    string Page(string t) => $"BT /F1 24 Tf 72 700 Td ({t}) Tj ET";
    var objs = new List<string>
    {
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Count 2 /Kids [3 0 R 4 0 R] >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        $"<< /Length {Page("Hello page one").Length} >>\nstream\n{Page("Hello page one")}\nendstream",
        $"<< /Length {Page("Second page here").Length} >>\nstream\n{Page("Second page here")}\nendstream",
    };
    var sb = new StringBuilder("%PDF-1.4\n"); var offs = new List<int>();
    for (int i = 0; i < objs.Count; i++) { offs.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
    int xref = sb.Length; sb.Append($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
    foreach (var o in offs) sb.Append($"{o:D10} 00000 n \n");
    sb.Append($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    File.WriteAllText(textPdf, sb.ToString(), Encoding.Latin1);
}
await T("pdf → txt, md, docx, html", async () =>
{
    NeedPdf();
    var t = await Run("pdf.to.txt", [textPdf]);
    var txt = File.ReadAllText(t.Outputs[0]);
    Assert(txt.Contains("Hello page one") && txt.Contains("Second page here"), "text: " + txt);
    await Run("pdf.to.md", [textPdf]); await Run("pdf.to.html", [textPdf]);
    var d = await Run("pdf.to.docx", [textPdf]);
    Assert(Documents.ToText(Documents.FromDocx(d.Outputs[0])).Contains("Second page here"), "docx text");
});
await T("pdf → jpg pages (folder), png", async () =>
{
    NeedPdf(); NeedFf();
    var j = await Run("pdf.to.jpg", [textPdf]);
    Assert(Directory.Exists(j.Outputs[0]) && Directory.GetFiles(j.Outputs[0]).Length == 2, "two page images in a folder");
    var info = await ff.InfoAsync(Directory.GetFiles(j.Outputs[0]).Order().First(), default);
    Assert(info.Width == 2550 && info.Height == 3300, $"300 dpi letter = {info.Width}x{info.Height}");
});
await T("pdf merge, split, extract, rotate", async () =>
{
    NeedPdf();
    var m = await Run("pdf.merge", [textPdf, textPdf]); Assert(Pdfium.PageCount(m.Outputs[0]) == 4, "merge");
    var s = await Run("pdf.split", [textPdf]); Assert(Directory.GetFiles(s.Outputs[0]).Length == 2, "split");
    var e = await Run("pdf.extract", [textPdf], new() { ["pages"] = "٢" });
    Assert(Pdfium.PageCount(e.Outputs[0]) == 1 && Pdfium.PageTexts(e.Outputs[0])[0].Contains("Second"), "extract arabic digit");
    var r = await Run("pdf.rotate", [textPdf]); var (_, w, h) = Pdfium.RenderPage(r.Outputs[0], 0, 72); Assert(w > h, "rotated");
    var bad = await queue.RunNowAsync(Op("pdf.extract"), [textPdf], new Dictionary<string, string> { ["pages"] = "9" });
    Assert(bad.State == JobState.Failed, "page 9 should fail");
});

Console.WriteLine("documents");
var docs = Dir("docs");
var md = Path.Combine(docs, "notes.md");
File.WriteAllText(md, "# مرحبا بالعالم\n\nهذا **نص** عربي مع English words.\n\n- بند أول\n- second item\n\n1. one\n2. two\n\n```\ncode = 1\n```\n\nA [link](https://example.com) and `code` and 3*4*5.\n");
await T("md → docx, html, txt", async () =>
{
    var d = await Run("doc.to.docx", [md]); var h = await Run("doc.to.html", [md]); var t = await Run("doc.to.txt", [md]);
    var back = Documents.FromDocx(d.Outputs[0]);
    Assert(back[0].Kind == BlockKind.Heading1 && back[0].Text == "مرحبا بالعالم", "heading kept: " + back[0]);
    using (var z = ZipFile.OpenRead(d.Outputs[0])) { var x = new StreamReader(z.GetEntry("word/document.xml")!.Open()).ReadToEnd(); Assert(x.Contains("<w:bidi/>"), "rtl paragraph"); }
    var html = File.ReadAllText(h.Outputs[0]);
    Assert(html.Contains("<strong>نص</strong>") && html.Contains("dir=\"rtl\"") && html.Contains("<a href=\"https://example.com\">link</a>") && html.Contains("<ol>"), "html");
    var txt = File.ReadAllText(t.Outputs[0]);
    Assert(txt.Contains("هذا نص عربي") && txt.Contains("• بند أول") && txt.Contains("3*4*5"), "txt: " + txt);
    await Task.CompletedTask;
});
await T("txt keeps literal stars and lines", async () =>
{
    var p = Path.Combine(docs, "plain.txt"); File.WriteAllText(p, "a *not italic* b\n\nline two\n");
    var d = await Run("doc.to.docx", [p]);
    var back = Documents.ToText(Documents.FromDocx(d.Outputs[0]));
    Assert(back.Contains("a *not italic* b") && back.Contains("line two"), back);
});
await T("windows-1256 text is read as Arabic", async () =>
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var p = Path.Combine(docs, "legacy.txt"); File.WriteAllBytes(p, Encoding.GetEncoding(1256).GetBytes("سلام"));
    Assert(Documents.ReadText(p) == "سلام", "decoded: " + Documents.ReadText(p));
    await Task.CompletedTask;
});
await T("html escapes and refuses script links", async () =>
{
    var html = Documents.ToHtml(Documents.FromMarkdown("<script>x</script> [a](javascript:alert(1))"), "t");
    Assert(!html.Contains("<script>x") && !html.Contains("javascript:"), html);
    await Task.CompletedTask;
});

Console.WriteLine("subtitles");
var srt = Path.Combine(docs, "movie.srt");
File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:02,500\nمرحبا\n\n2\n00:00:03,000 --> 00:00:04,000\n<i>Second</i> line\nwraps\n\n");
await T("srt → vtt → srt, txt, shift", async () =>
{
    var v = await Run("sub.to.vtt", [srt]);
    var vtt = File.ReadAllText(v.Outputs[0]);
    Assert(vtt.StartsWith("WEBVTT") && vtt.Contains("00:00:01.000 --> 00:00:02.500"), vtt);
    var back = await Run("sub.to.srt", [v.Outputs[0]]);
    Assert(File.ReadAllText(back.Outputs[0]).Contains("00:00:03,000 --> 00:00:04,000"), "roundtrip");
    var t = await Run("sub.to.txt", [srt]); Assert(File.ReadAllText(t.Outputs[0]) == "مرحبا\nSecond line wraps\n", File.ReadAllText(t.Outputs[0]));
    var s = await Run("sub.shift", [srt], new() { ["seconds"] = "-1.5" });
    var shifted = File.ReadAllText(s.Outputs[0]);
    Assert(shifted.Contains("00:00:00,000 --> 00:00:01,000") && shifted.Contains("00:00:01,500 --> 00:00:02,500"), shifted);
});

Console.WriteLine("archives");
await T("zip folder, extract, repack", async () =>
{
    var d = Dir("arc"); var folder = Path.Combine(d, "Project"); Directory.CreateDirectory(Path.Combine(folder, "sub"));
    File.WriteAllText(Path.Combine(folder, "a.txt"), "A"); File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "B");
    var z = await Run("act.zip", [folder]); Assert(Path.GetFileName(z.Outputs[0]) == "Project.zip", z.Outputs[0]);
    var x = await Run("arc.extract", [z.Outputs[0]]);
    Assert(Path.GetFileName(x.Outputs[0]) == "Project 2" && File.ReadAllText(Path.Combine(x.Outputs[0], "sub", "b.txt")) == "B", "extract " + x.Outputs[0]);
    var tgz = await Run("arc.to.tgz", [z.Outputs[0]]); Assert(tgz.Outputs[0].EndsWith(".tar.gz"), tgz.Outputs[0]);
    var back = await Run("arc.to.zip", [tgz.Outputs[0]]);
    using var za = ZipFile.OpenRead(back.Outputs[0]);
    Assert(za.Entries.Any(e => e.FullName == "Project/sub/b.txt"), string.Join(",", za.Entries.Select(e => e.FullName)));
});
await T("zip slip is refused", async () =>
{
    var d = Dir("slip"); var evil = Path.Combine(d, "evil.zip");
    using (var fs = File.Create(evil)) using (var za = new ZipArchive(fs, ZipArchiveMode.Create)) { using var s = new StreamWriter(za.CreateEntry("../../escaped.txt").Open()); s.Write("x"); }
    var job = await queue.RunNowAsync(Op("arc.extract"), [evil]);
    Assert(job.State == JobState.Failed && !File.Exists(Path.Combine(root, "escaped.txt")), "slip " + job.State);
});

Console.WriteLine();
Console.WriteLine($"{pass} passed, {fail} failed, {skip} skipped");
try { if (fail == 0) Directory.Delete(root, true); else Console.WriteLine("fixtures kept in " + root); } catch { }
return fail == 0 ? 0 : 1;

sealed class SkipException(string m) : Exception(m);
