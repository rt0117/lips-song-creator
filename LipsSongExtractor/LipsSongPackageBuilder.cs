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
            // Zurueckschreiben, damit BuildDlcXml den PreviewLyric-Fallback nutzen kann
            input.LyricText = lyricText;
        }

        pkg.Files[$"{safeTitle}_Lyric.X360"] = BuildLyricX360(safeTitle, lyricText);

        // 3. DLC.xml
        pkg.Files["DLC.xml"] = BuildDlcXml(input, safeTitle);

        return pkg;
    }

    /// <summary>
    /// Erzeugt eine Lyric.X360 Datei (ixRawFileImage mit dem Liedtext).
    /// </summary>
    // Klassen-Indizes im Original-Lyric-Header (Resources/LyricHeader.xml, 1-basiert)
    private const int LYR_CLS_PACKAGE = 4;        // ixPackage (72)
    private const int LYR_CLS_DBLCNT = 8;         // ixDblCnt<ixPackage*> (12)
    private const int LYR_CLS_ASSET_PACKAGE = 10; // ixAssetPackage (92)
    private const int LYR_CLS_RAW_FILE = 15;      // ixRawFileImage (84)

    public static byte[] BuildLyricX360(string title, string lyricText)
    {
        var lyricName = $"{title}_Lyric";
        var typeName = "Text";

        // UTF-8 BOM + Text
        var textBytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(lyricText))
            .ToArray();

        // Struktur exakt wie Original (Happy Ending_Lyric.X360, 12 Eintraege):
        //  [0] inline  Name ("X_Lyric\0")
        //  [1] inline  "Text\0"
        //  [2] inline  Text-Daten (BOM + Lyrics)
        //  [3] inline  Name nochmal (fuer Root-Package)
        //  [4] inline  128B Asset-Array (Slot 0 -> rawFile)
        //  [5] inline  "Text\0" (zweite Kopie)
        //  [6] ixRawFileImage (84)
        //  [7] ixDblCnt leer (next/prev = self)
        //  [8] ixAssetPackage (92)
        //  [9] ixDblCnt (value -> assetPkg, next/prev -> [10])
        // [10] ixDblCnt (value=0, next/prev -> [9])
        // [11] ixPackage Root (72, LETZTER Eintrag)
        var builder = new IxbBlobBuilder();

        var namePtr = builder.AddString(lyricName);
        var nameLen = Encoding.UTF8.GetByteCount(lyricName + "\0");
        var typePtr = builder.AddString(typeName);
        var typeLen = Encoding.UTF8.GetByteCount(typeName + "\0");
        var textPtr = builder.AddInlineData(textBytes);
        var name2Ptr = builder.AddString(lyricName);

        var assetArray = new byte[128];
        var assetArrayPtr = builder.AddInlineData(assetArray);

        var type2Ptr = builder.AddString(typeName);

        // ixRawFileImage (84):
        //   +4 refCount, +8 m_strName, +24 m_pAssetPackage, +28 m_UserColor(FF),
        //   +36 m_aHash (16B), +52 m_vData, +68 m_strTypeName
        var rawFile = builder.AddObject(LYR_CLS_RAW_FILE, 84);
        rawFile.WriteU32(4, 1);
        rawFile.WriteStringVector(8, namePtr, nameLen);
        rawFile.WriteU32(28, 0x000000FF); // m_UserColor
        rawFile.WriteStringVector(52, textPtr, textBytes.Length);
        rawFile.WriteStringVector(68, typePtr, typeLen);

        // Asset-Array Slot 0 -> rawFile
        assetArray[0] = (byte)(rawFile.Ptr >> 24);
        assetArray[1] = (byte)(rawFile.Ptr >> 16);
        assetArray[2] = (byte)(rawFile.Ptr >> 8);
        assetArray[3] = (byte)rawFile.Ptr;

        // Leerer Listenknoten des AssetPackage
        var pkgListNode = builder.AddObject(LYR_CLS_DBLCNT, 12);
        pkgListNode.WriteU32(0, 0);
        pkgListNode.WriteU32(4, pkgListNode.Ptr);
        pkgListNode.WriteU32(8, pkgListNode.Ptr);

        // ixAssetPackage (92)
        var assetPkg = builder.AddObject(LYR_CLS_ASSET_PACKAGE, 92);
        assetPkg.WriteU32(4, 1);
        // m_pParent @ 8 -> rootPkg (unten)
        assetPkg.WriteU32(12, pkgListNode.Ptr);
        assetPkg.WriteStringVector(24, type2Ptr, typeLen); // Original: Name = "Text"
        assetPkg.WriteU32(40, 1); // m_bIsLoaded
        assetPkg.WriteU32(72, assetArrayPtr); // m_vpAssets._data
        assetPkg.WriteU32(76, 0x20);
        assetPkg.WriteU32(80, 1);

        rawFile.WriteU32(24, assetPkg.Ptr); // m_pAssetPackage -> assetPkg

        // Root-Kinderliste (Sentinel-Paar)
        var rootListNode = builder.AddObject(LYR_CLS_DBLCNT, 12);
        var tailNode = builder.AddObject(LYR_CLS_DBLCNT, 12);
        rootListNode.WriteU32(0, assetPkg.Ptr);
        rootListNode.WriteU32(4, tailNode.Ptr);
        rootListNode.WriteU32(8, tailNode.Ptr);
        tailNode.WriteU32(0, 0);
        tailNode.WriteU32(4, rootListNode.Ptr);
        tailNode.WriteU32(8, rootListNode.Ptr);

        // Root-ixPackage (72, letzter Eintrag)
        var rootPkg = builder.AddObject(LYR_CLS_PACKAGE, 72);
        rootPkg.WriteU32(4, 1);
        rootPkg.WriteU32(8, 0);
        rootPkg.WriteU32(12, tailNode.Ptr);
        rootPkg.WriteU32(16, 1);
        rootPkg.WriteStringVector(24, name2Ptr, nameLen);
        rootPkg.WriteU32(40, 1);
        rootPkg.WriteData(48, [0x01]);

        assetPkg.WriteU32(8, rootPkg.Ptr);

        var blob = builder.Build();

        // Original-Lyric-Header als Embedded Resource
        var asm = typeof(LipsSongPackageBuilder).Assembly;
        using var stream = asm.GetManifestResourceStream("LipsSongExtractor.Resources.LyricHeader.xml")
            ?? throw new InvalidOperationException("LyricHeader.xml Resource fehlt.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var xml = reader.ReadToEnd().Replace("{NUM_ELEMENTS}", builder.EntryCount.ToString());
        var headerBytes = Encoding.UTF8.GetBytes(xml);
        return BuildX360File(headerBytes, blob);
    }

    /// <summary>
    /// Erzeugt die DLC.xml die das Spiel zum Auffinden des Songs braucht.
    /// </summary>
    public static byte[] BuildDlcXml(SongInput input, string safeTitle)
    {
        // Generiere eine eindeutige Offer-ID (8 hex chars)
        var offerId = GenerateOfferId(input.Title, input.Artist);
        var uintId = $"0x{offerId}";
        var contentId = $"4D530888{offerId}"; // Muss genau 16 Zeichen sein (TitleID 8 + OfferID 8)

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

            // Tilde = Tonhoehenwechsel derselben Silbe (kein Text)
            currentLine.Append(note.Text.Replace("~", ""));
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
        // 8 Hex-Zeichen (32-bit), mit fuehrender Null falls noetig
        // Muss exakt 8 Zeichen sein, da ChartContentID = TitleID(8) + offerID(8) = 16 Zeichen
        var hash = (uint)Environment.TickCount;
        foreach (var c in title + artist)
            hash = hash * 31 + c;
        return $"{hash:X8}";
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
