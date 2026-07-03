using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Konvertiert einen UltraStar-Song in eine Lips .X360-Datei.
///
/// Erzeugt:
/// - XML-Header mit allen benoetigten Klassen-Definitionen
/// - Binary Blob mit lpsChart, ixSequence, Markern etc.
///
/// Klassen-Layout (Offsets aus California Love Referenz):
///   lpsChart (184):    m_strName@8, m_vpSequence@76, m_vpExtraSequence@92, 
///                      m_MusicStartOffset@108, m_strNoiseMaker@112, m_BaseCentOffset@160,
///                      m_strAudioEffectPresetPath@168
///   ixSequence (104):  m_strName@8, m_vpSeqCode@72, m_vpListeners@88
///   lpsMelodyMarker (40): m_fTriggerTiming@8, m_fLength@12, m_iTrackIndex@16,
///                         m_bTriggered@20, m_Tone@24 (fIdx@0+octave@4), m_bTilt@32, m_pLyricMarker@36
///   lpsLyricMarker (64):  m_fTriggerTiming@8, m_fLength@12, m_iTrackIndex@16,
///                         m_bTriggered@20, m_pMelodyMarker@24, m_vecLyricWordData@28,
///                         m_strFreeWord@44, m_bEndOfWord@60
///   lpsHitMarker (48):    m_fTriggerTiming@8, m_fLength@12, m_iTrackIndex@16,
///                         m_bTriggered@20, m_Tone@24, m_bTilt@32, m_pLyricMarker@36
///   lpsPhraseMarker (40): m_fTriggerTiming@8, m_fLength@12, m_bTriggered@20,
///                         m_Tone@24, m_bTilt@32, m_pLyricMarker@36
///   ixSeqNameTag (36):    m_fTriggerTiming@8, m_fLength@12, m_strTagName@20
/// </summary>
public static class UltraStarToLipsConverter
{
    // Klassen-Groessen (aus California Love Referenz)
    private const int SZ_CHART = 184;
    private const int SZ_SEQUENCE = 104;
    private const int SZ_MELODY_MARKER = 40;
    private const int SZ_LYRIC_MARKER = 64;
    private const int SZ_HIT_MARKER = 48;
    private const int SZ_PHRASE_MARKER = 40;
    private const int SZ_NAME_TAG = 36;
    private const int SZ_PAGE_BREAK = 24;

    /// <summary>
    /// Konvertiert einen UltraStar-Song in die Roh-Bestandteile einer .X360-Datei.
    /// </summary>
    public static (byte[] headerBytes, byte[] blob) Convert(UltraStarSong song)
    {
        var builder = new IxbBlobBuilder();

        // ── Strings registrieren ──────────────────────────────
        var titlePtr = builder.AddString(song.Title);
        var titleLen = Encoding.UTF8.GetByteCount(song.Title + "\0");

        var noiseMakerPtr = builder.AddString("Set003");
        var noiseMakerLen = Encoding.UTF8.GetByteCount("Set003\0");

        var effectPtr = builder.AddString("Effect01.xml");
        var effectLen = Encoding.UTF8.GetByteCount("Effect01.xml\0");

        var emptyStrPtr = builder.AddString("");
        var emptyStrLen = 1; // null terminator

        // ── Noten verarbeiten ─────────────────────────────────
        var singableP1 = song.SingableNotes.Where(n => n.Player == 1).ToList();
        var phrasesP1 = song.Notes.Where(n => n.Type == UltraNoteType.PhraseBreak && n.Player == 1).ToList();

        // Melody-Marker erstellen
        var melodyPtrs = new List<uint>();
        var melodyWriters = new List<ObjectWriter>();
        foreach (var note in singableP1)
        {
            var w = builder.AddObject(SZ_MELODY_MARKER);
            w.WriteU32(4, 1); // m_uiReferenceCount
            w.WriteF32(8, song.BeatToSeconds(note.StartBeat)); // m_fTriggerTiming
            w.WriteF32(12, song.BeatsToSeconds(note.Length)); // m_fLength
            w.WriteI32(16, 0); // m_iTrackIndex
            w.WriteU32(20, 0); // m_bTriggered
            // m_Tone (inline Tone struct at offset 24)
            w.WriteF32(24, note.LipsFIdx); // fIdx
            w.WriteI32(28, note.LipsOctave); // octave
            w.WriteU32(32, 0); // m_bTilt
            w.WriteU32(36, 0); // m_pLyricMarker (null - wird spaeter verlinkt)

            melodyPtrs.Add(w.Ptr);
            melodyWriters.Add(w);
        }

        // Lyric-Marker erstellen
        var lyricPtrs = new List<uint>();
        for (var i = 0; i < singableP1.Count; i++)
        {
            var note = singableP1[i];
            var text = note.Text.TrimEnd();
            var isEndOfWord = note.Text.EndsWith(" ") || note.Text.EndsWith("-") ||
                              i + 1 >= singableP1.Count;

            var textPtr = string.IsNullOrEmpty(text) ? emptyStrPtr : builder.AddString(text);
            var textLen = Encoding.UTF8.GetByteCount((string.IsNullOrEmpty(text) ? "" : text) + "\0");

            var w = builder.AddObject(SZ_LYRIC_MARKER);
            w.WriteU32(4, 1); // m_uiReferenceCount
            w.WriteF32(8, song.BeatToSeconds(note.StartBeat)); // m_fTriggerTiming
            w.WriteF32(12, song.BeatsToSeconds(note.Length)); // m_fLength
            w.WriteI32(16, 0); // m_iTrackIndex
            w.WriteU32(20, 0); // m_bTriggered
            w.WriteU32(24, melodyPtrs[i]); // m_pMelodyMarker -> zum zugehoerigen MelodyMarker
            // m_vecLyricWordData at 28 (16 bytes, leer lassen)
            w.WriteStringVector(44, textPtr, textLen); // m_strFreeWord
            w.WriteU32(60, isEndOfWord ? 1u : 0u); // m_bEndOfWord

            lyricPtrs.Add(w.Ptr);

            // Melody-Marker zurueckverlinken
            melodyWriters[i].WriteU32(36, w.Ptr); // m_pLyricMarker
        }

        // Phrase-Marker erstellen
        var phrasePtrs = new List<uint>();
        foreach (var pb in phrasesP1)
        {
            var w = builder.AddObject(SZ_PHRASE_MARKER);
            w.WriteU32(4, 1);
            w.WriteF32(8, song.BeatToSeconds(pb.StartBeat));
            w.WriteF32(12, 0.1f);
            phrasePtrs.Add(w.Ptr);
        }

        // Time NameTags
        var beatTagPtr = builder.AddString($"Beat {(int)(song.RealBpm / 8)}");
        var beatTagLen = Encoding.UTF8.GetByteCount($"Beat {(int)(song.RealBpm / 8)}\0");
        var pvStartPtr = builder.AddString("PV Start");
        var pvStartLen = Encoding.UTF8.GetByteCount("PV Start\0");
        var stopPtr = builder.AddString("Stop");
        var stopLen = Encoding.UTF8.GetByteCount("Stop\0");

        var timeTag1 = builder.AddObject(SZ_NAME_TAG);
        timeTag1.WriteU32(4, 1);
        timeTag1.WriteF32(8, 0);
        timeTag1.WriteF32(12, 0.08f);
        timeTag1.WriteStringVector(20, beatTagPtr, beatTagLen);

        var timeTag2 = builder.AddObject(SZ_NAME_TAG);
        timeTag2.WriteU32(4, 1);
        timeTag2.WriteF32(8, song.GapMs / 1000f);
        timeTag2.WriteF32(12, 0.08f);
        timeTag2.WriteStringVector(20, pvStartPtr, pvStartLen);

        var timeTag3 = builder.AddObject(SZ_NAME_TAG);
        timeTag3.WriteU32(4, 1);
        timeTag3.WriteF32(8, song.DurationSeconds + 2);
        timeTag3.WriteF32(12, 0.08f);
        timeTag3.WriteStringVector(20, stopPtr, stopLen);

        // ── Sequenzen erstellen ───────────────────────────────

        // Melody-Sequenz
        var melodySeqName = builder.AddString("Melody");
        var melodyCodeArrayPtr = builder.AddPointerArray(melodyPtrs.ToArray());
        var melodySeq = BuildSequence(builder, melodySeqName,
            Encoding.UTF8.GetByteCount("Melody\0"),
            melodyCodeArrayPtr, melodyPtrs.Count, emptyStrPtr, emptyStrLen);

        // Lyric-Sequenz
        var lyricSeqName = builder.AddString("Lyric");
        var lyricCodeArrayPtr = builder.AddPointerArray(lyricPtrs.ToArray());
        var lyricSeq = BuildSequence(builder, lyricSeqName,
            Encoding.UTF8.GetByteCount("Lyric\0"),
            lyricCodeArrayPtr, lyricPtrs.Count, emptyStrPtr, emptyStrLen);

        // Time-Sequenz
        var timeSeqName = builder.AddString("Time");
        var timeCodePtrs = new[] { timeTag1.Ptr, timeTag2.Ptr, timeTag3.Ptr };
        var timeCodeArrayPtr = builder.AddPointerArray(timeCodePtrs);
        var timeSeq = BuildSequence(builder, timeSeqName,
            Encoding.UTF8.GetByteCount("Time\0"),
            timeCodeArrayPtr, 3, emptyStrPtr, emptyStrLen);

        // Section-Sequenz (Phrasen)
        var sectionSeqName = builder.AddString("Section");
        uint sectionCodeArrayPtr = 0;
        if (phrasePtrs.Count > 0)
            sectionCodeArrayPtr = builder.AddPointerArray(phrasePtrs.ToArray());
        var sectionSeq = BuildSequence(builder, sectionSeqName,
            Encoding.UTF8.GetByteCount("Section\0"),
            sectionCodeArrayPtr, phrasePtrs.Count, emptyStrPtr, emptyStrLen);

        // Leere Pflicht-Sequenzen
        var conductorSeq = BuildEmptySequence(builder, "Conductor", emptyStrPtr, emptyStrLen);
        var audioSeq = BuildEmptySequence(builder, "Audio", emptyStrPtr, emptyStrLen);
        var groupSeq = BuildEmptySequence(builder, "Group", emptyStrPtr, emptyStrLen);
        var movieSeq = BuildEmptySequence(builder, "Movie", emptyStrPtr, emptyStrLen);

        // ── Haupt-Sequenz-Array ───────────────────────────────
        var seqPtrs = new[]
        {
            timeSeq, conductorSeq, audioSeq,
            lyricSeq, melodySeq,
            sectionSeq, groupSeq, movieSeq
        };
        var seqArrayPtr = builder.AddPointerArray(seqPtrs);

        // ── lpsChart erstellen ────────────────────────────────
        var chart = builder.AddObject(SZ_CHART);
        chart.WriteU32(4, 1); // m_uiReferenceCount
        chart.WriteStringVector(8, titlePtr, titleLen); // m_strName
        // m_pAssetPackage @ 24 = null
        // m_UserColor @ 28 = 0
        // m_aHash @ 36 (16 bytes, leer)
        // strStateName @ 56 (leerer String)
        chart.WriteStringVector(56, emptyStrPtr, emptyStrLen);
        chart.WriteVector(76, seqArrayPtr, seqPtrs.Length); // m_vpSequence
        // m_vpExtraSequence @ 92 (leer)
        chart.WriteU32(108, 0); // m_MusicStartOffset
        chart.WriteStringVector(112, noiseMakerPtr, noiseMakerLen); // m_strNoiseMaker
        chart.WriteStringVector(128, noiseMakerPtr, noiseMakerLen); // m_strNoiseMakerForLS2
        // m_BaseCentOffset @ 160
        chart.WriteU32(160, 0);
        // m_strAudioEffectPresetPath @ 168
        chart.WriteStringVector(168, effectPtr, effectLen);

        // ── Blob bauen ────────────────────────────────────────
        var blob = builder.Build();

        // ── XML Header generieren ─────────────────────────────
        var header = GenerateXmlHeader(blob.Length, builder.EntryCount);

        return (header, blob);
    }

    private static uint BuildSequence(IxbBlobBuilder builder, uint namePtr, int nameLen,
        uint codeArrayPtr, int codeCount, uint emptyStrPtr, int emptyStrLen)
    {
        var seq = builder.AddObject(SZ_SEQUENCE);
        seq.WriteU32(4, 1); // m_uiReferenceCount
        seq.WriteStringVector(8, namePtr, nameLen); // m_strName
        seq.WriteStringVector(56, emptyStrPtr, emptyStrLen); // strStateName
        if (codeArrayPtr != 0)
            seq.WriteVector(72, codeArrayPtr, codeCount); // m_vpSeqCode
        // m_vpListeners @ 88 (leer)
        return seq.Ptr;
    }

    private static uint BuildEmptySequence(IxbBlobBuilder builder, string name,
        uint emptyStrPtr, int emptyStrLen)
    {
        var namePtr = builder.AddString(name);
        var nameLen = Encoding.UTF8.GetByteCount(name + "\0");
        return BuildSequence(builder, namePtr, nameLen, 0, 0, emptyStrPtr, emptyStrLen);
    }

    private static byte[] GenerateXmlHeader(int blobSize, int numElements)
    {
        var sb = new StringBuilder();
        sb.Append($"<ixb IsBigEndian=\"true\" IsText=\"false\" Platform=\"WIN32\" NumOfElements=\"{numElements}\">");
        sb.Append("<Classes>");

        // Minimale Klassen-Definitionen (basierend auf California Love Referenz)
        AddClass(sb, "ixObject", 0, 4);
        AddClass(sb, "ixReferencedObject", 1, 8, ("m_uiReferenceCount", 4));
        AddClass(sb, "ixTreeNode<ixPackage>", 2, 24);
        AddClass(sb, "ixPackage", 3, 72);
        // Vector-Typen (keine Members, nur Size)
        AddClass(sb, "ixVector<char,1,ixAllocator<char,1>,ixIterator<char> >", 0, 16,
            ("_data", 0), ("_reserve", 4), ("_size", 8), ("_allocator", 12));
        AddClass(sb, "ixVector<ixSequence *,1,ixAllocator<ixSequence *,1>,ixIterator<ixSequence *> >", 0, 16,
            ("_data", 0), ("_reserve", 4), ("_size", 8), ("_allocator", 12));
        AddClass(sb, "ixVector<ixSeqCode *,1,ixAllocator<ixSeqCode *,1>,ixIterator<ixSeqCode *> >", 0, 16,
            ("_data", 0), ("_reserve", 4), ("_size", 8), ("_allocator", 12));
        // Asset-Klassen
        AddClass(sb, "ixAsset", 2, 52,
            ("m_strName", 8), ("m_pAssetPackage", 24), ("m_UserColor", 28), ("m_aHash", 36));
        AddClass(sb, "ixPrototype", 8, 56);
        AddClass(sb, "ixAgentPrototype", 9, 72);
        AddClass(sb, "ixChart", 10, 108,
            ("m_vpSequence", 76), ("m_vpExtraSequence", 92), ("m_MusicStartOffset", 108));
        AddClass(sb, "lpsChart", 11, 184,
            ("m_strNoiseMaker", 112), ("m_strNoiseMakerForLS2", 128),
            ("m_BaseCentOffset", 160), ("m_pIndex", 144), ("m_pMusicData", 148),
            ("m_strAudioEffectPresetPath", 168), ("m_strLyricPathCash", 152));
        // Sequenz-Klassen
        AddClass(sb, "ixSequence", 10, 104,
            ("m_vpSeqCode", 72), ("m_vpListeners", 88));
        AddClass(sb, "ixSeqCode", 2, 20,
            ("m_fTriggerTiming", 8), ("m_fLength", 12), ("m_iTrackIndex", 16));
        AddClass(sb, "ixSeqUtilCode", 14, 20);
        AddClass(sb, "ixSeqNameTag", 15, 36, ("m_strTagName", 20));
        AddClass(sb, "ixSeqContentSpecific", 14, 20);
        AddClass(sb, "ixSeqMarkerCode", 17, 20);
        AddClass(sb, "lpsMarker", 18, 24, ("m_bTriggered", 20));
        AddClass(sb, "lpsMelodyMarker", 19, 40,
            ("m_Tone", 24), ("m_bTilt", 32), ("m_pLyricMarker", 36));
        AddClass(sb, "lpsLyricMarker", 19, 64,
            ("m_pMelodyMarker", 24), ("m_vecLyricWordData", 28),
            ("m_strFreeWord", 44), ("m_bEndOfWord", 60));
        AddClass(sb, "Tone", 0, 8, ("fIdx", 0), ("octave", 4));
        AddClass(sb, "lpsPhraseMarker", 20, 40);
        AddClass(sb, "lpsHitMarker", 20, 48, ("m_Vowel", 44));
        AddClass(sb, "lpsPageBreakMarker", 19, 24);

        sb.Append("</Classes>");
        // <Objects> tag wird vom Writer hinzugefuegt
        return Encoding.UTF8.GetBytes(sb.ToString());
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
