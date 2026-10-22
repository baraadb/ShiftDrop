using System.IO.Compression;
using System.Text;

namespace ShiftDrop.Core.Engines;

/// <summary>
/// Writes a PowerPoint file with one full-slide picture per page (PDF → PPTX, pictures → slides).
/// The smallest package PowerPoint, Keynote, LibreOffice and Google Slides all open.
/// </summary>
public static class PptxWriter
{
    const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    const string Ns = $"xmlns:a=\"{A}\" xmlns:r=\"{R}\" xmlns:p=\"{P}\"";
    const string RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    const string Group = "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    /// <param name="slides">JPEG bytes with the picture's pixel size, in order.</param>
    public static void Write(IReadOnlyList<(byte[] Jpeg, int Width, int Height)> slides, string path, string title)
    {
        if (slides.Count == 0) throw new EngineException("No pages");
        // slide size follows the first page: 13.333 in wide, height by its shape (PowerPoint allows 1–56 in)
        const long Emu = 914400;
        long cx = 12192000;
        long cy = (long)Math.Round(cx * (double)slides[0].Height / slides[0].Width);
        if (cy > 56 * Emu) { cy = 56 * Emu; cx = (long)Math.Round(cy * (double)slides[0].Width / slides[0].Height); }
        cx = Math.Clamp(cx, Emu, 56 * Emu);
        cy = Math.Clamp(cy, Emu, 56 * Emu);

        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Put(string name, string xml)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        string Rels(params (string Id, string Type, string Target)[] rels) =>
            Head + $"<Relationships xmlns=\"{RelNs}\">" + string.Concat(rels.Select(r => $"<Relationship Id=\"{r.Id}\" Type=\"{RelBase}{r.Type}\" Target=\"{r.Target}\"/>")) + "</Relationships>";

        var types = new StringBuilder(Head + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
            "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>" +
            "<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>" +
            "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>" +
            "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
            "<Override PartName=\"/ppt/presProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presProps+xml\"/>" +
            "<Override PartName=\"/ppt/viewProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml\"/>" +
            "<Override PartName=\"/ppt/tableStyles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
        for (int i = 1; i <= slides.Count; i++)
            types.Append($"<Override PartName=\"/ppt/slides/slide{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
        types.Append("</Types>");
        Put("[Content_Types].xml", types.ToString());

        Put("_rels/.rels", Head + $"<Relationships xmlns=\"{RelNs}\"><Relationship Id=\"rId1\" Type=\"{RelBase}officeDocument\" Target=\"ppt/presentation.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
        Put("docProps/core.xml", Head + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            $"<dc:title>{Documents.XmlEscape(title)}</dc:title><dc:creator>ShiftDrop</dc:creator></cp:coreProperties>");

        // presentation
        var ids = new StringBuilder();
        var presRels = new List<(string, string, string)> { ("rId1", "slideMaster", "slideMasters/slideMaster1.xml") };
        for (int i = 1; i <= slides.Count; i++)
        {
            ids.Append($"<p:sldId id=\"{255 + i}\" r:id=\"rId{i + 1}\"/>");
            presRels.Add(($"rId{i + 1}", "slide", $"slides/slide{i}.xml"));
        }
        int n = slides.Count + 2;
        presRels.Add(($"rId{n}", "theme", "theme/theme1.xml"));
        presRels.Add(($"rId{n + 1}", "presProps", "presProps.xml"));
        presRels.Add(($"rId{n + 2}", "viewProps", "viewProps.xml"));
        presRels.Add(($"rId{n + 3}", "tableStyles", "tableStyles.xml"));
        Put("ppt/presentation.xml", Head + $"<p:presentation {Ns} saveSubsetFonts=\"1\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:sldIdLst>{ids}</p:sldIdLst><p:sldSz cx=\"{cx}\" cy=\"{cy}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        Put("ppt/_rels/presentation.xml.rels", Rels(presRels.ToArray()));
        Put("ppt/presProps.xml", Head + $"<p:presentationPr {Ns}/>");
        Put("ppt/viewProps.xml", Head + $"<p:viewPr {Ns}><p:normalViewPr><p:restoredLeft sz=\"15620\"/><p:restoredTop sz=\"94660\"/></p:normalViewPr><p:gridSpacing cx=\"76200\" cy=\"76200\"/></p:viewPr>");
        Put("ppt/tableStyles.xml", Head + $"<a:tblStyleLst xmlns:a=\"{A}\" def=\"{{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}}\"/>");

        // master, layout, theme
        Put("ppt/slideMasters/slideMaster1.xml", Head + $"<p:sldMaster {Ns}><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>{Group}</p:spTree></p:cSld>" +
            "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>");
        Put("ppt/slideMasters/_rels/slideMaster1.xml.rels", Rels(("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml"), ("rId2", "theme", "../theme/theme1.xml")));
        Put("ppt/slideLayouts/slideLayout1.xml", Head + $"<p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\"><p:spTree>{Group}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        Put("ppt/slideLayouts/_rels/slideLayout1.xml.rels", Rels(("rId1", "slideMaster", "../slideMasters/slideMaster1.xml")));
        Put("ppt/theme/theme1.xml", Head + Theme);

        // slides: the picture fitted and centred
        for (int i = 1; i <= slides.Count; i++)
        {
            var (jpeg, w, h) = slides[i - 1];
            double scale = Math.Min((double)cx / w, (double)cy / h);
            long pw = (long)Math.Round(w * scale), ph = (long)Math.Round(h * scale);
            long x = (cx - pw) / 2, y = (cy - ph) / 2;
            var media = zip.CreateEntry($"ppt/media/image{i}.jpeg", CompressionLevel.NoCompression);
            using (var s = media.Open()) s.Write(jpeg);
            Put($"ppt/slides/slide{i}.xml", Head + $"<p:sld {Ns}><p:cSld><p:spTree>{Group}" +
                $"<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"Page {i}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                "<p:blipFill><a:blip r:embed=\"rId2\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                $"<p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{pw}\" cy=\"{ph}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            Put($"ppt/slides/_rels/slide{i}.xml.rels", Rels(("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml"), ("rId2", "image", $"../media/image{i}.jpeg")));
        }
    }

    const string Theme = $"<a:theme xmlns:a=\"{A}\" name=\"ShiftDrop\"><a:themeElements>" +
        "<a:clrScheme name=\"ShiftDrop\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"1F2937\"/></a:dk2><a:lt2><a:srgbClr val=\"E5E7EB\"/></a:lt2><a:accent1><a:srgbClr val=\"2563EB\"/></a:accent1><a:accent2><a:srgbClr val=\"0EA5E9\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"16A34A\"/></a:accent3><a:accent4><a:srgbClr val=\"F97316\"/></a:accent4><a:accent5><a:srgbClr val=\"8B5CF6\"/></a:accent5><a:accent6><a:srgbClr val=\"EC4899\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"2563EB\"/></a:hlink><a:folHlink><a:srgbClr val=\"7C3AED\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"ShiftDrop\"><a:majorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont><a:minorFont><a:latin typeface=\"Segoe UI\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"ShiftDrop\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
        "<a:lnStyleLst><a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme>" +
        "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
}
