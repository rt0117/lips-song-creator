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
    // Klassen-Groessen (aus California Love / Happy Ending Referenz)
    private const int SZ_CHART = 184;
    private const int SZ_SEQUENCE = 104;
    private const int SZ_MELODY_MARKER = 40;
    private const int SZ_LYRIC_MARKER = 64;
    private const int SZ_HIT_MARKER = 48;
    private const int SZ_PHRASE_MARKER = 40;
    private const int SZ_NAME_TAG = 36;
    private const int SZ_PAGE_BREAK = 24;
    private const int SZ_MARKER = 24;             // lpsMarker (Section)
    private const int SZ_TEMPO_CODE = 44;         // ixSeqTempoCode
    private const int SZ_AUDIO_EFFECT_SEQ = 120;  // ixAudioEffectSequence
    private const int SZ_LED_MASTER_SEQ = 140;    // lpsLedMasterSequence
    private const int SZ_PACKAGE = 72;            // ixPackage
    private const int SZ_ASSET_PACKAGE = 92;      // ixAssetPackage
    private const int SZ_DBLCNT = 12;             // ixDblCnt<ixPackage*>

    // Klassen-Indizes: 1-basierte Position in der <Classes>-Liste des
    // Original-Headers (Resources/ChartHeader.xml). Der Loader nutzt diesen
    // Index am Anfang jedes Blob-Eintrags, um die Klasse zu instanziieren.
    private const int CLS_PACKAGE = 4;
    private const int CLS_DBLCNT = 8;
    private const int CLS_ASSET_PACKAGE = 10;
    private const int CLS_CHART = 22;
    private const int CLS_SEQUENCE = 25;
    private const int CLS_NAME_TAG = 28;
    private const int CLS_TEMPO_MAP = 29;
    private const int CLS_TEMPO_CODE = 31;
    private const int CLS_AUDIO_MARKER = 34;
    private const int CLS_MARKER = 36;
    private const int CLS_MELODY_MARKER = 37;
    private const int CLS_LYRIC_MARKER = 38;
    private const int CLS_PHRASE_MARKER = 40;     // Melodie-Noten in Original-DLCs!
    private const int CLS_PAGE_BREAK = 41;        // Section-Marker in Original-DLCs
    private const int CLS_SHORT_END = 42;         // 1x am Song-Ende
    private const int CLS_AUDIO_EFFECT_SEQ = 46;
    private const int CLS_LED_MASTER_SEQ = 48;

    private const int SZ_AUDIO_MARKER = 36;       // ixAudioMarker
    private const int SZ_SHORT_END = 24;          // lpsShortEndMarker

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
        var singableP2 = song.SingableNotes.Where(n => n.Player == 2).ToList();
        if (singableP2.Count == 0) singableP2 = singableP1; // kein Duett -> P1 doppelt
        var phrasesP1 = song.Notes.Where(n => n.Type == UltraNoteType.PhraseBreak && n.Player == 1).ToList();

        // Marker fuer Solo (P1), Duet (P1) und Duet_P2 erzeugen.
        // Original-Charts haben eigene Marker-Objekte pro Sequenz (keine geteilten
        // Pointer), da das Spiel m_bTriggered pro Marker zur Laufzeit setzt.
        var (melodyPtrs, lyricPtrs) = BuildMarkers(builder, song, singableP1, emptyStrPtr);
        var (melodyDuetPtrs, lyricDuetPtrs) = BuildMarkers(builder, song, singableP1, emptyStrPtr);
        var (melodyP2Ptrs, lyricP2Ptrs) = BuildMarkers(builder, song, singableP2, emptyStrPtr);

        // Section-Marker: lpsMarker (24 Bytes) an Phrasengrenzen (wie Original)
        var sectionPtrs = new List<uint>();
        foreach (var pb in phrasesP1)
        {
            var w = builder.AddObject(CLS_MARKER, SZ_MARKER);
            w.WriteU32(4, 1);              // m_uiReferenceCount
            w.WriteF32(8, song.BeatToSeconds(pb.StartBeat));
            w.WriteF32(12, 0.0781f);       // m_fLength (wie Original)
            w.WriteI32(16, 0);             // m_iTrackIndex
            w.WriteU32(20, 0);             // m_bTriggered
            sectionPtrs.Add(w.Ptr);
        }

        // Audio: ixAudioMarker triggert die PV-Wiedergabe bei GAP ("PV Start").
        // KRITISCH: Ohne diesen Marker crasht das Spiel beim PV-Start-Trigger.
        // Pfad-Format wie Original-DLCs (relativer Asset-Pfad auf {Title}_PV).
        var pvPath = $"../../../../../Lps/Suite102/Images/lps/Levels/Intl/M/{song.Artist}/{song.Title}/{song.Title}_PV";
        var pvPathPtr = builder.AddString(pvPath);
        var pvPathLen = Encoding.UTF8.GetByteCount(pvPath + "\0");

        var audioMarker = builder.AddObject(CLS_AUDIO_MARKER, SZ_AUDIO_MARKER);
        audioMarker.WriteU32(4, 1);                     // m_uiReferenceCount
        audioMarker.WriteF32(8, song.GapMs / 1000f);    // m_fTriggerTiming = PV Start
        audioMarker.WriteF32(12, song.DurationSeconds); // m_fLength = Song-Dauer
        audioMarker.WriteI32(16, 0);                    // m_iTrackIndex
        audioMarker.WriteStringVector(20, pvPathPtr, pvPathLen); // m_strAudioName

        // Time NameTags ("Beat8" ist ein festes Tag, kein BPM-Wert - siehe Original)
        var beatTagPtr = builder.AddString("Beat8");
        var beatTagLen = Encoding.UTF8.GetByteCount("Beat8\0");
        var pvStartPtr = builder.AddString("PV Start");
        var pvStartLen = Encoding.UTF8.GetByteCount("PV Start\0");
        var stopPtr = builder.AddString("Stop");
        var stopLen = Encoding.UTF8.GetByteCount("Stop\0");

        var timeTag1 = builder.AddObject(CLS_NAME_TAG, SZ_NAME_TAG);
        timeTag1.WriteU32(4, 1);
        timeTag1.WriteF32(8, 0);
        timeTag1.WriteF32(12, 0.078f);
        timeTag1.WriteStringVector(20, beatTagPtr, beatTagLen);

        var timeTag2 = builder.AddObject(CLS_NAME_TAG, SZ_NAME_TAG);
        timeTag2.WriteU32(4, 1);
        timeTag2.WriteF32(8, song.GapMs / 1000f);
        timeTag2.WriteF32(12, 0.078f);
        timeTag2.WriteStringVector(20, pvStartPtr, pvStartLen);

        var timeTag3 = builder.AddObject(CLS_NAME_TAG, SZ_NAME_TAG);
        timeTag3.WriteU32(4, 1);
        timeTag3.WriteF32(8, song.DurationSeconds + 2);
        timeTag3.WriteF32(12, 0.078f);
        timeTag3.WriteStringVector(20, stopPtr, stopLen);

        // Conductor: ixSeqTempoCode mit BPM (KRITISCH - ohne Tempo laedt der Song nicht)
        var tempoCode = builder.AddObject(CLS_TEMPO_CODE, SZ_TEMPO_CODE);
        tempoCode.WriteU32(4, 1);              // m_uiReferenceCount
        tempoCode.WriteF32(8, 0);              // m_fTriggerTiming
        tempoCode.WriteF32(12, 0.0625f);       // m_fLength (wie Original)
        tempoCode.WriteI32(16, 1);             // m_iTrackIndex (Original: 1)
        tempoCode.WriteF32(20, song.RealBpm);  // m_Tempo (float BPM)
        tempoCode.WriteI32(24, 4);             // m_Numerator (4/4-Takt)
        tempoCode.WriteI32(28, 4);             // m_Denominator
        tempoCode.WriteI32(32, 0);             // m_CalculatedMeasure
        tempoCode.WriteI32(36, 0);             // m_CalculatedBeat
        tempoCode.WriteI32(40, 0);             // m_CalculatedTick

        // ── Sequenzen erstellen (Original-Reihenfolge, 15 Stueck) ──
        uint Seq(string name, List<uint> codes, int clsIdx = CLS_SEQUENCE, int size = SZ_SEQUENCE)
        {
            var namePtr = builder.AddString(name);
            var nameLen = Encoding.UTF8.GetByteCount(name + "\0");
            uint arrayPtr = 0;
            if (codes.Count > 0)
                arrayPtr = AddPointerArrayWithCapacity(builder, codes);
            return BuildSequence(builder, namePtr, nameLen, arrayPtr, codes.Count,
                emptyStrPtr, emptyStrLen, clsIdx, size);
        }

        var timeSeq = Seq("Time", [timeTag1.Ptr, timeTag2.Ptr, timeTag3.Ptr]);
        // Conductor ist eine ixTempoMap (cls=29, Size=104 wie ixSequence)!
        var conductorSeq = Seq("Conductor", [tempoCode.Ptr], CLS_TEMPO_MAP);
        var audioSeq = Seq("Audio", [audioMarker.Ptr]);
        var lyricSeq = Seq("Lyric", lyricPtrs);
        var melodySeq = Seq("Melody", melodyPtrs);
        var lyricDuetSeq = Seq("Lyric_Duet", lyricDuetPtrs);
        var melodyDuetSeq = Seq("Melody_Duet", melodyDuetPtrs);
        var lyricP2Seq = Seq("Lyric_Duet_P2", lyricP2Ptrs);
        var melodyP2Seq = Seq("Melody_Duet_P2", melodyP2Ptrs);
        var sectionSeq = Seq("Section", sectionPtrs);
        var groupSeq = Seq("Group", []);
        var carSeq = Seq("CallAndResponse", []);
        var movieSeq = Seq("Movie", []);
        // AudioEffect (120 Bytes: eigener Typ mit Extra-Vector @104, leer)
        var audioFxSeq = Seq("AudioEffect", [], CLS_AUDIO_EFFECT_SEQ, SZ_AUDIO_EFFECT_SEQ);
        // Led (140 Bytes: lpsLedMasterSequence mit Led-Vectors @104/@120, leer)
        var ledSeq = Seq("Led", [], CLS_LED_MASTER_SEQ, SZ_LED_MASTER_SEQ);

        // ── Haupt-Sequenz-Array (Reihenfolge wie Original!) ────
        var seqPtrs = new[]
        {
            timeSeq, conductorSeq, audioSeq,
            lyricSeq, melodySeq,
            lyricDuetSeq, melodyDuetSeq,
            lyricP2Seq, melodyP2Seq,
            sectionSeq, groupSeq, carSeq, movieSeq,
            audioFxSeq, ledSeq
        };
        var seqArrayPtr = builder.AddPointerArray(seqPtrs);

        // ── Extra-Sequenzen (6 Stueck, alle leer) ──────────────
        var extraSeqPtrs = new[]
        {
            Seq("TimedGesture", []),
            Seq("TimedGesture_Duet", []),
            Seq("TimedGesture_Duet_P2", []),
            Seq("Noisemaker_Mic", []),
            Seq("Noisemaker_Mic_Duet", []),
            Seq("Noisemaker_Mic_Duet_P2", []),
        };
        var extraSeqArrayPtr = builder.AddPointerArray(extraSeqPtrs);

        // ── Package-Baum + lpsChart (exakt wie Original-Ende) ──
        // Original-Struktur der letzten 7 Eintraege:
        //   [N-7] inline 128B    Asset-Array (Slot 0 = chartPtr, Rest 0)
        //   [N-6] inline "lpsChart\0"  Name des AssetPackage
        //   [N-5] lpsChart       m_pAssetPackage -> assetPkg
        //   [N-4] ixDblCnt       Listenknoten (leer: next/prev auf sich selbst)
        //   [N-3] ixAssetPackage m_vpAssets -> Asset-Array, Parent -> rootPkg
        //   [N-2] ixDblCnt       Root-Listenknoten .next/.prev -> [N-1]
        //   [N-1] ixDblCnt       Kind-Listenknoten, .value -> assetPkg
        //   [N]   ixPackage      Root, m_lstpChildren -> [N-1], Name = Titel

        // Asset-Array (128 Bytes = 32 Pointer-Slots, nur Slot 0 belegt)
        var assetArray = new byte[128];
        var assetArrayPtr = builder.AddInlineData(assetArray); // chartPtr wird unten gepatcht

        var chartPkgNamePtr = builder.AddString("lpsChart");
        var chartPkgNameLen = Encoding.UTF8.GetByteCount("lpsChart\0");

        // Feld-Offsets exakt aus dem Original-Header (ChartHeader.xml):
        //   ixAsset:  m_strName@8, m_pAssetPackage@24, m_UserColor@28, m_aHash@36
        //   ixPrototype: strStateName@56 (aus Original-Hexdump)
        //   ixChart:  m_vpSequence@72, m_vpExtraSequence@88, m_MusicStartOffset@104
        //   lpsChart: m_strNoiseMaker@108, m_strNoiseMakerForLS2@124,
        //             m_BaseCentOffset@140, m_pIndex@144, m_pMusicData@148,
        //             m_strAudioEffectPresetPath@152, m_strLyricPathCash@168
        var chart = builder.AddObject(CLS_CHART, SZ_CHART);
        chart.WriteU32(4, 1); // m_uiReferenceCount
        chart.WriteStringVector(8, titlePtr, titleLen); // m_strName
        chart.WriteU32(28, 0x000000FF); // m_UserColor (wie Original)
        // m_aHash @ 36 (20 bytes, leer)
        chart.WriteStringVector(56, emptyStrPtr, emptyStrLen); // strStateName
        chart.WriteVector(72, seqArrayPtr, seqPtrs.Length, 0x20); // m_vpSequence (15)
        chart.WriteVector(88, extraSeqArrayPtr, extraSeqPtrs.Length, 0x20); // m_vpExtraSequence (6)
        chart.WriteU32(104, 0); // m_MusicStartOffset
        chart.WriteStringVector(108, noiseMakerPtr, noiseMakerLen); // m_strNoiseMaker
        chart.WriteStringVector(124, noiseMakerPtr, noiseMakerLen); // m_strNoiseMakerForLS2
        chart.WriteU32(140, 0); // m_BaseCentOffset
        // m_pIndex @ 144 = null, m_pMusicData @ 148 = null
        chart.WriteStringVector(152, effectPtr, effectLen); // m_strAudioEffectPresetPath
        // m_strLyricPathCash @ 168 = leer (wie Original)

        // Asset-Array Slot 0 -> chart (nachtraeglich patchen)
        assetArray[0] = (byte)(chart.Ptr >> 24);
        assetArray[1] = (byte)(chart.Ptr >> 16);
        assetArray[2] = (byte)(chart.Ptr >> 8);
        assetArray[3] = (byte)chart.Ptr;

        // Leerer ixDblCnt-Listenknoten des AssetPackage
        // (Original [5478]: value=0, next=self, prev=self)
        var pkgListNode = builder.AddObject(CLS_DBLCNT, SZ_DBLCNT);
        pkgListNode.WriteU32(0, 0);
        pkgListNode.WriteU32(4, pkgListNode.Ptr);
        pkgListNode.WriteU32(8, pkgListNode.Ptr);

        // ixAssetPackage (92 Bytes) - Original [5479]:
        //   +4  refCount, +8 m_pParent (-> rootPkg), +12 m_lstpChildren (-> pkgListNode)
        //   +24 m_strName ("lpsChart"), +40 m_bIsLoaded=1
        //   +72 m_vpAssets (_data -> assetArray, _reserve=0x20, _size=1)
        var assetPkg = builder.AddObject(CLS_ASSET_PACKAGE, SZ_ASSET_PACKAGE);
        assetPkg.WriteU32(4, 1);   // refCount
        // m_pParent @ 8 wird unten auf rootPkg gesetzt
        assetPkg.WriteU32(12, pkgListNode.Ptr); // m_lstpChildren
        assetPkg.WriteStringVector(24, chartPkgNamePtr, chartPkgNameLen); // m_strName
        assetPkg.WriteU32(40, 1);  // m_bIsLoaded (Original: +40 = 1)
        assetPkg.WriteU32(72, assetArrayPtr);  // m_vpAssets._data
        assetPkg.WriteU32(76, 0x20);           // _reserve (32 Slots)
        assetPkg.WriteU32(80, 1);              // _size (1 Asset: lpsChart)

        // chart.m_pAssetPackage @ 24 -> assetPkg (Rueckverweis wie Original)
        chart.WriteU32(24, assetPkg.Ptr);

        // Root-Listenknoten (Original [5480]): value -> assetPkg, next/prev -> tailNode
        // Tail-Knoten   (Original [5481]): value=0, next/prev -> rootListNode
        // (Doppelt verkettete Liste mit Sentinel)
        var rootListNode = builder.AddObject(CLS_DBLCNT, SZ_DBLCNT);
        var tailNode = builder.AddObject(CLS_DBLCNT, SZ_DBLCNT);
        rootListNode.WriteU32(0, assetPkg.Ptr);
        rootListNode.WriteU32(4, tailNode.Ptr);
        rootListNode.WriteU32(8, tailNode.Ptr);
        tailNode.WriteU32(0, 0);
        tailNode.WriteU32(4, rootListNode.Ptr);
        tailNode.WriteU32(8, rootListNode.Ptr);

        // Root-ixPackage (72 Bytes, MUSS letzter Eintrag sein!) - Original [5482]:
        //   +4 refCount, +8 m_pParent=0, +12 m_lstpChildren -> tailNode
        //   +16 ??? = 1, +24 m_strName (Titel), +40 UserColor, +44 m_bIsLoaded=1, +48 Flag=0x01
        var rootPkg = builder.AddObject(CLS_PACKAGE, SZ_PACKAGE);
        rootPkg.WriteU32(4, 1);    // refCount
        rootPkg.WriteU32(8, 0);    // m_pParent = null (Root)
        rootPkg.WriteU32(12, tailNode.Ptr); // m_lstpChildren
        rootPkg.WriteU32(16, 1);   // wie Original (+16 = 1)
        rootPkg.WriteStringVector(24, titlePtr, titleLen); // m_strName
        rootPkg.WriteU32(40, 1);   // m_bIsLoaded (Original: +40 = 1)
        rootPkg.WriteData(48, [0x01]); // Flag @ 48 (Original: 01 00 00 00)

        // assetPkg.m_pParent -> rootPkg
        assetPkg.WriteU32(8, rootPkg.Ptr);

        // ── Blob bauen ────────────────────────────────────────
        var blob = builder.Build();

        // ── XML Header: Original-Header 1:1 verwenden ─────────
        var header = GetChartHeader(builder.EntryCount);

        return (header, blob);
    }

    /// <summary>
    /// Laedt den Original-Chart-XML-Header (Happy Ending) als Embedded Resource
    /// und setzt NumOfElements. Damit sind alle 58 Klassen-Definitionen
    /// byte-identisch zum Original - keine Abweichungen im Klassen-Layout.
    /// </summary>
    private static byte[] GetChartHeader(int numElements)
    {
        var asm = typeof(UltraStarToLipsConverter).Assembly;
        using var stream = asm.GetManifestResourceStream("LipsSongExtractor.Resources.ChartHeader.xml")
            ?? throw new InvalidOperationException("ChartHeader.xml Resource fehlt.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var xml = reader.ReadToEnd().Replace("{NUM_ELEMENTS}", numElements.ToString());
        return Encoding.UTF8.GetBytes(xml);
    }

    /// <summary>
    /// Erzeugt Melody- + Lyric-Marker fuer eine Notenliste und verlinkt sie gegenseitig.
    /// </summary>
    private static (List<uint> melodyPtrs, List<uint> lyricPtrs) BuildMarkers(
        IxbBlobBuilder builder, UltraStarSong song, List<UltraStarNote> notes, uint emptyStrPtr)
    {
        var melodyPtrs = new List<uint>();
        var melodyWriters = new List<ObjectWriter>();
        foreach (var note in notes)
        {
            var w = builder.AddObject(CLS_MELODY_MARKER, SZ_MELODY_MARKER);
            w.WriteU32(4, 1); // m_uiReferenceCount
            w.WriteF32(8, song.BeatToSeconds(note.StartBeat)); // m_fTriggerTiming
            w.WriteF32(12, song.BeatsToSeconds(note.Length)); // m_fLength
            w.WriteI32(16, 0); // m_iTrackIndex
            w.WriteU32(20, 0); // m_bTriggered
            // m_Tone (inline Tone struct at offset 24)
            w.WriteF32(24, note.LipsFIdx); // fIdx
            w.WriteI32(28, note.LipsOctave); // octave
            w.WriteU32(32, 0); // m_bTilt
            w.WriteU32(36, 0); // m_pLyricMarker (wird unten verlinkt)

            melodyPtrs.Add(w.Ptr);
            melodyWriters.Add(w);
        }

        var lyricPtrs = new List<uint>();
        for (var i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            var text = note.Text.TrimEnd();
            var isEndOfWord = note.Text.EndsWith(" ") || note.Text.EndsWith("-") ||
                              i + 1 >= notes.Count;

            var textPtr = string.IsNullOrEmpty(text) ? emptyStrPtr : builder.AddString(text);
            var textLen = Encoding.UTF8.GetByteCount((string.IsNullOrEmpty(text) ? "" : text) + "\0");

            var w = builder.AddObject(CLS_LYRIC_MARKER, SZ_LYRIC_MARKER);
            w.WriteU32(4, 1); // m_uiReferenceCount
            w.WriteF32(8, song.BeatToSeconds(note.StartBeat)); // m_fTriggerTiming
            w.WriteF32(12, song.BeatsToSeconds(note.Length)); // m_fLength
            w.WriteI32(16, 0); // m_iTrackIndex
            w.WriteU32(20, 0); // m_bTriggered
            w.WriteU32(24, melodyPtrs[i]); // m_pMelodyMarker
            // m_vecLyricWordData at 28 (16 bytes, leer)
            w.WriteStringVector(44, textPtr, textLen); // m_strFreeWord
            w.WriteU32(60, isEndOfWord ? 1u : 0u); // m_bEndOfWord

            lyricPtrs.Add(w.Ptr);
            melodyWriters[i].WriteU32(36, w.Ptr); // m_pLyricMarker zurueckverlinken
        }

        return (melodyPtrs, lyricPtrs);
    }

    /// <summary>
    /// Pointer-Array mit auf 0x20 aufgerundeter Kapazitaet (wie Original:
    /// _reserve ist immer die naechste Zweierpotenz >= 0x20).
    /// </summary>
    private static uint AddPointerArrayWithCapacity(IxbBlobBuilder builder, List<uint> pointers)
    {
        var capacity = 0x20;
        while (capacity < pointers.Count) capacity *= 2;
        var data = new byte[capacity * 4];
        for (var i = 0; i < pointers.Count; i++)
        {
            data[i * 4] = (byte)(pointers[i] >> 24);
            data[i * 4 + 1] = (byte)(pointers[i] >> 16);
            data[i * 4 + 2] = (byte)(pointers[i] >> 8);
            data[i * 4 + 3] = (byte)pointers[i];
        }
        return builder.AddInlineData(data);
    }

    private static uint BuildSequence(IxbBlobBuilder builder, uint namePtr, int nameLen,
        uint codeArrayPtr, int codeCount, uint emptyStrPtr, int emptyStrLen,
        int clsIdx = CLS_SEQUENCE, int size = SZ_SEQUENCE)
    {
        var seq = builder.AddObject(clsIdx, size);
        seq.WriteU32(4, 1); // m_uiReferenceCount
        seq.WriteStringVector(8, namePtr, nameLen); // m_strName
        seq.WriteU32(28, 0x000000FF); // m_UserColor (wie Original)
        // strStateName @ 56: Original hat Flag @52=1 bei gefuellten Sequenzen
        if (codeCount > 0) seq.WriteU32(52, 1);
        seq.WriteStringVector(56, emptyStrPtr, emptyStrLen); // strStateName

        // KRITISCH: Auch LEERE Vektoren brauchen gueltige _data-Pointer!
        // Original: alle Sequenzen haben m_vpSeqCode/m_vpListeners mit
        // _reserve=0x20 und echtem Buffer - Null-Pointer crashen das Spiel.
        if (codeArrayPtr == 0)
            codeArrayPtr = builder.AddInlineData(new byte[0x20 * 4]);
        var codeCapacity = 0x20;
        while (codeCapacity < codeCount) codeCapacity *= 2;
        seq.WriteVector(72, codeArrayPtr, codeCount, codeCapacity); // m_vpSeqCode

        var listenersPtr = builder.AddInlineData(new byte[0x20 * 4]);
        seq.WriteVector(88, listenersPtr, 0, 0x20); // m_vpListeners (leer)

        // Extra-Vectors der Spezial-Sequenzen ebenfalls mit gueltigen Buffern
        if (size >= SZ_AUDIO_EFFECT_SEQ)
        {
            var extraPtr = builder.AddInlineData(new byte[0x20 * 4]);
            seq.WriteVector(104, extraPtr, 0, 0x20); // m_vecPresetEntrySequences / m_vecpLedSequence
        }
        if (size >= SZ_LED_MASTER_SEQ)
        {
            var extra2Ptr = builder.AddInlineData(new byte[0x20 * 4]);
            seq.WriteVector(120, extra2Ptr, 0, 0x20); // m_vecpExtraLedSequence
        }

        return seq.Ptr;
    }

}
