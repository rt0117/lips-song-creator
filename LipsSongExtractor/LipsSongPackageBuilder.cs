using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Erzeugt ein komplettes Lips-Song-Paket (alle Dateien die fuer einen DLC noetig sind).
/// 
/// Paket-Inhalt:
///   DLC.xml              - Song-Discovery (MusicIndex + MusicVideo)
///   {Title}.X360         - Chart (Noten, Lyrics, Timing)
///   {Title}_Lyric.X360   - Liedtext als ixRawFileImage
///   {Title}.jpg           - Album-Cover (wird vom Aufrufer bereitgestellt)
///   {Title}.xWMA          - Audio (wird vom Aufrufer bereitgestellt)
///   {Title}_prv.xWMA      - Audio-Preview (wird vom Aufrufer bereitgestellt)
/// </summary>
public static class LipsSongPackageBuilder
{
    /// <summary>
    /// Daten die der Aufrufer bereitstellen muss.
    /// </summary>
    public class SongInput
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Album { get; set; } = "";
        public string Genre { get; set; } = "Pop";
        public string Year { get; set; } = "2024";
        public string Language { get; set; } = "EN";
        public int LengthSeconds { get; set; } = 180;
        public string LyricText { get; set; } = "";
        public string PreviewLyric { get; set; } = "";

        /// <summary>
        /// UltraStar-Song (wenn vorhanden) fuer Chart-Generierung.
        /// </summary>
        public UltraStarSong? UltraStarSong { get; set; }
    }

    /// <summary>
    /// Ergebnis der Paket-Generierung: Dateiname -> Byte-Inhalt.
    /// Audio/Cover muessen vom Aufrufer hinzugefuegt werden.
    /// </summary>
    public class SongPackage
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public string Title { get; set; } = "";
    }

    /// <summary>
    /// Erzeugt ein Song-Paket mit allen generierbaren Dateien.
    /// </summary>
    public static SongPackage Build(SongInput input)
    {
        var pkg = new SongPackage { Title = input.Title };
        var safeTitle = input.Title; // Fuer Dateinamen

        // 1. Chart (.X360)
        if (input.UltraStarSong != null)
        {
            var (chartHeader, chartBlob) = UltraStarToLipsConverter.Convert(input.UltraStarSong);
            var chartFile = BuildX360File(chartHeader, chartBlob);
            pkg.Files[$"{safeTitle}.X360"] = chartFile;
        }

        // 2. Lyric.X360
        var lyricText = input.LyricText;
        if (string.IsNullOrEmpty(lyricText) && input.UltraStarSong != null)
        {
            lyricText = BuildLyricTextFromUltraStar(input.UltraStarSong);
        }

        pkg.Files[$"{safeTitle}_Lyric.X360"] = BuildLyricX360(safeTitle, lyricText);

        // 3. DLC.xml
        pkg.Files["DLC.xml"] = BuildDlcXml(input, safeTitle);

        return pkg;
    }

    /// <summary>
    /// Erzeugt eine Lyric.X360 Datei (ixRawFileImage mit dem Liedtext).
    /// </summary>
    public static byte[] BuildLyricX360(string title, string lyricText)
    {
        var lyricName = $"{title}_Lyric";
        var typeName = "Text";

        // UTF-8 BOM + Text
        var textBytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(lyricText))
            .ToArray();

        var builder = new IxbBlobBuilder();

        // Strings
        var namePtr = builder.AddString(lyricName);
        var nameLen = Encoding.UTF8.GetByteCount(lyricName + "\0");
        var typePtr = builder.AddString(typeName);
        var typeLen = Encoding.UTF8.GetByteCount(typeName + "\0");
        var textPtr = builder.AddInlineData(textBytes);
        var emptyPtr = builder.AddString("");

        // ixRawFileImage (Size=84)
        // Offsets aus California Love / From Yesterday Referenz:
        //   4: m_uiReferenceCount
        //   8: m_strName (ixVector<char>, 16B)
        //  24: m_pAssetPackage (ptr, 4B)
        //  28: m_UserColor (4B)
        //  36: m_aHash (16B)
        //  52: m_vData (ixVector<char>, 16B) = Dateiinhalt
        //  68: m_strTypeName (ixVector<char>, 16B)
        var rawFile = builder.AddObject(84);
        rawFile.WriteU32(4, 1); // refCount
        rawFile.WriteStringVector(8, namePtr, nameLen);
        // m_pAssetPackage: null (24)
        // m_UserColor: 0 (28)
        // m_aHash: 0 (36-51)
        rawFile.WriteStringVector(52, textPtr, textBytes.Length);
        rawFile.WriteStringVector(68, typePtr, typeLen);

        // ixAssetPackage (Size=92) - Container
        var pkgNamePtr = builder.AddString(lyricName);
        var assetPkg = builder.AddObject(92);
        assetPkg.WriteU32(4, 1);
        assetPkg.WriteStringVector(24, pkgNamePtr, nameLen);

        // ixPackage (Size=72) - Root-Paket
        var rootPkg = builder.AddObject(72);
        rootPkg.WriteU32(4, 1);
        rootPkg.WriteStringVector(24, pkgNamePtr, nameLen);

        var blob = builder.Build();

        // XML-Header fuer Lyric-Datei (minimaler Header)
        var headerSb = new StringBuilder();
        headerSb.Append($"<ixb IsBigEndian=\"true\" IsText=\"false\" Platform=\"WIN32\" NumOfElements=\"{builder.EntryCount}\">");
        headerSb.Append("<Classes>");
        AddClass(headerSb, "ixObject", 0, 4);
        AddClass(headerSb, "ixReferencedObject", 1, 8, ("m_uiReferenceCount", 4));
        AddClass(headerSb, "ixTreeNode<ixPackage>", 2, 24);
        AddClass(headerSb, "ixPackage", 3, 72);
        AddClass(headerSb, "ixList<ixPackage *,ixAllocator<ixDblCnt<ixPackage *>,1> >", 0, 12);
        AddClass(headerSb, "ixVector<char,1,ixAllocator<char,1>,ixIterator<char> >", 0, 16,
            ("_data", 0), ("_reserve", 4), ("_size", 8), ("_allocator", 12));
        AddClass(headerSb, "ixVector<ixPackage *,1,ixAllocator<ixPackage *,1>,ixIterator<ixPackage *> >", 0, 16,
            ("_data", 0), ("_reserve", 4), ("_size", 8), ("_allocator", 12));
        AddClass(headerSb, "ixDblCnt<ixPackage *>", 0, 12);
        AddClass(headerSb, "ixVector<ixAsset *,1,ixAllocator<ixAsset *,1>,ixIterator<ixAsset *> >", 0, 16,
            ("_data", 0), ("_reserve", 4), ("_size", 8), ("_allocator", 12));
        AddClass(headerSb, "ixAssetPackage", 4, 92);
        AddClass(headerSb, "ixMxColor4Base<unsigned char,unsigned int>", 0, 4);
        AddClass(headerSb, "ixMxCharColor4", 11, 4);
        AddClass(headerSb, "ixAsset", 2, 52,
            ("m_pAssetPackage", 24), ("m_strName", 8), ("m_UserColor", 28), ("m_aHash", 36));
        AddClass(headerSb, "ixFileImage", 13, 68, ("m_vData", 52));
        AddClass(headerSb, "ixRawFileImage", 14, 84, ("m_strTypeName", 68));
        headerSb.Append("</Classes>");

        var headerBytes = Encoding.UTF8.GetBytes(headerSb.ToString());
        return BuildX360File(headerBytes, blob);
    }

    /// <summary>
    /// Erzeugt die DLC.xml die das Spiel zum Auffinden des Songs braucht.
    /// </summary>
    public static byte[] BuildDlcXml(SongInput input, string safeTitle)
    {
        // Generiere eine eindeutige Offer-ID (7 hex chars)
        var offerId = GenerateOfferId(input.Title, input.Artist);
        var uintId = $"0x{offerId}";
        var contentId = $"4D530888{offerId}";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<DLCContents>");
        sb.AppendLine("  <MusicIndices>");
        sb.AppendLine("    <MusicIndex>");
        sb.AppendLine($"      <Artist>{EscapeXml(input.Artist)}</Artist>");
        sb.AppendLine($"      <Title>{EscapeXml(input.Title)}</Title>");
        sb.AppendLine($"      <Genre>{EscapeXml(input.Genre)}</Genre>");
        sb.AppendLine($"      <Year>{input.Year}</Year>");
        sb.AppendLine($"      <Language>{input.Language}</Language>");
        sb.AppendLine($"      <Album>{EscapeXml(input.Album)}</Album>");
        sb.AppendLine($"      <Length>{input.LengthSeconds}</Length>");
        sb.AppendLine("      <Rating>0</Rating>");
        sb.AppendLine("      <LeaderBoardID>9999</LeaderBoardID>");
        sb.AppendLine($"      <ChartUri>{safeTitle}.X360</ChartUri>");
        sb.AppendLine($"      <AudioUri>{safeTitle}.xWMA</AudioUri>");
        sb.AppendLine($"      <LyricUri>{safeTitle}_Lyric.X360</LyricUri>");
        sb.AppendLine($"      <AlbumJacketUri>{safeTitle}.jpg</AlbumJacketUri>");
        sb.AppendLine($"      <PreviewAudioUri>{safeTitle}_prv.xWMA</PreviewAudioUri>");
        sb.AppendLine($"      <offerID>{offerId}</offerID>");
        sb.AppendLine($"      <UintID>{uintId}</UintID>");
        sb.AppendLine($"      <ChartContentID>{contentId}</ChartContentID>");
        sb.AppendLine($"      <VideoContentID>{contentId}</VideoContentID>");

        var preview = input.PreviewLyric;
        if (string.IsNullOrEmpty(preview) && !string.IsNullOrEmpty(input.LyricText))
        {
            preview = input.LyricText.Length > 80
                ? input.LyricText[..80]
                : input.LyricText;
            preview = preview.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        sb.AppendLine($"      <PreviewLyric>\"{EscapeXml(preview)}\"</PreviewLyric>");
        sb.AppendLine("    </MusicIndex>");
        sb.AppendLine("  </MusicIndices>");
        sb.AppendLine("  <MusicVideos />");
        sb.AppendLine("  <LicenseBits ValidBits=\"3\">0x7</LicenseBits>");
        sb.AppendLine("</DLCContents>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Baut den Lyric-Text aus einem UltraStar-Song zusammen.
    /// Gruppiert Silben zu Wörtern und Phrasen.
    /// </summary>
    private static string BuildLyricTextFromUltraStar(UltraStarSong song)
    {
        var sb = new StringBuilder();
        var currentLine = new StringBuilder();

        foreach (var note in song.Notes.Where(n => n.Player == 1))
        {
            if (note.Type == UltraNoteType.PhraseBreak)
            {
                if (currentLine.Length > 0)
                {
                    sb.AppendLine(currentLine.ToString().Trim());
                    currentLine.Clear();
                }

                continue;
            }

            currentLine.Append(note.Text);
        }

        if (currentLine.Length > 0)
            sb.AppendLine(currentLine.ToString().Trim());

        return sb.ToString();
    }

    private static byte[] BuildX360File(byte[] headerBytes, byte[] blob)
    {
        using var ms = new MemoryStream();
        ms.Write(headerBytes);
        ms.Write(Encoding.ASCII.GetBytes("<Objects>"));
        ms.Write(blob);
        ms.Write(Encoding.ASCII.GetBytes("</Objects></ixb>"));
        return ms.ToArray();
    }

    private static string GenerateOfferId(string title, string artist)
    {
        var hash = 0u;
        foreach (var c in title + artist)
            hash = hash * 31 + c;
        return $"{(hash & 0x0FFFFFFF):X7}";
    }

    private static void AddClass(StringBuilder sb, string name, int baseIdx, int size,
        params (string name, int offset)[] members)
    {
        sb.Append($"<Class Name=\"{EscapeXml(name)}\"");
        if (baseIdx > 0) sb.Append($" Base=\"{baseIdx}\"");
        sb.Append($" Size=\"{size}\">");
        sb.Append("<Members>");
        foreach (var (mName, mOffset) in members)
            sb.Append($"<Member Name=\"{mName}\" Offset=\"{mOffset}\"/>");
        sb.Append("</Members></Class>");
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
