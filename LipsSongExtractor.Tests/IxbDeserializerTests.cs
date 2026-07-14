namespace LipsSongExtractor.Tests;

public class IxbDeserializerTests
{
    // ── Pointer-Lookup ──────────────────────────────────────────

    [Fact]
    public void Lyric_PointerLookup_ContainsStringEntries()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        // Der Blob muss den Pointer 0x3A3806A0 -> "California Love_Lyric" enthalten
        Assert.True(deser.PointerLookup.ContainsKey(0x3A3806A0));
        var entry = deser.PointerLookup[0x3A3806A0];
        Assert.Equal(23, entry.Size);
    }

    [Fact]
    public void Lyric_PointerLookup_ContainsTextString()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        // Mindestens ein Entry muss "Text" enthalten
        var textEntries = deser.PointerLookup.Values
            .Where(e => e.Size == 5)
            .ToList();
        Assert.NotEmpty(textEntries);
    }

    // ── Objekt-Erkennung ────────────────────────────────────────

    [Fact]
    public void Lyric_FindsMultipleObjects()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        Assert.True(deser.Objects.Count >= 3,
            $"Erwartet mindestens 3 Objekte, gefunden: {deser.Objects.Count}");
    }

    [Fact]
    public void Lyric_FindsIxRawFileImage()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var rawFile = deser.Objects.FirstOrDefault(o => o.ClassName == "ixRawFileImage");
        Assert.NotNull(rawFile);
        Assert.Equal(84, rawFile.Size);
    }

    [Fact]
    public void Lyric_FindsIxAssetPackage()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var assetPkg = deser.Objects.FirstOrDefault(o => o.ClassName == "ixAssetPackage");
        Assert.NotNull(assetPkg);
        Assert.Equal(92, assetPkg.Size);
    }

    [Fact]
    public void Lyric_FindsIxPackage()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var pkg = deser.Objects.FirstOrDefault(o => o.ClassName == "ixPackage");
        Assert.NotNull(pkg);
        Assert.Equal(72, pkg.Size);
    }

    // ── Feld-Auflösung: Lyric ixRawFileImage ────────────────────

    [Fact]
    public void Lyric_RawFileImage_HasCorrectName()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var obj = deser.FindAndReadObject("ixRawFileImage");
        Assert.NotNull(obj);
        Assert.Equal("California Love_Lyric", obj["m_strName"]);
    }

    [Fact]
    public void Lyric_RawFileImage_HasCorrectTypeName()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var obj = deser.FindAndReadObject("ixRawFileImage");
        Assert.NotNull(obj);
        Assert.Equal("Text", obj["m_strTypeName"]);
    }

    [Fact]
    public void Lyric_RawFileImage_HasReferenceCount1()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var obj = deser.FindAndReadObject("ixRawFileImage");
        Assert.NotNull(obj);
        Assert.Equal(1u, obj["m_uiReferenceCount"]);
    }

    [Fact]
    public void Lyric_RawFileImage_VDataContainsLyrics()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var obj = deser.FindAndReadObject("ixRawFileImage");
        Assert.NotNull(obj);

        var vData = obj["m_vData"] as string;
        Assert.NotNull(vData);
        Assert.Contains("California", vData);
        Assert.Contains("knows how to party", vData);
        Assert.Contains("West Side", vData);
    }

    [Fact]
    public void Lyric_RawFileImage_HashIs16Bytes()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var obj = deser.FindAndReadObject("ixRawFileImage");
        Assert.NotNull(obj);

        var hash = obj["m_aHash"] as byte[];
        Assert.NotNull(hash);
        Assert.Equal(16, hash.Length);
        // Hash sollte nicht nur Nullen sein
        Assert.True(hash.Any(b => b != 0), "Hash sollte nicht komplett leer sein");
    }

    // ── Haupt-X360: Objekt-Erkennung ────────────────────────────

    [Fact]
    public void Main_FindsLpsChart()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var chart = deser.Objects.FirstOrDefault(o => o.ClassName == "lpsChart");
        Assert.NotNull(chart);
        Assert.Equal(184, chart.Size);
    }

    [Fact]
    public void Main_FindsMelodyMarkers()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        // California Love (Disc-Song) nutzt lpsHitMarker/lpsPhraseMarker
        // (beide erben von lpsMelodyMarker). Der alte Groessen-heuristische
        // Parser hatte diese faelschlich als lpsMelodyMarker klassifiziert -
        // der strikte Parser liest die echten Klassen-Indizes aus dem Blob.
        var melodyFamily = deser.Objects.Where(o =>
            o.ClassName is "lpsMelodyMarker" or "lpsHitMarker" or "lpsPhraseMarker").ToList();
        Assert.True(melodyFamily.Count > 50,
            $"Erwartet viele Melodie-Marker (inkl. Hit/Phrase), gefunden: {melodyFamily.Count}");
    }

    [Fact]
    public void Main_FindsLyricMarkers()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var lyrics = deser.Objects.Where(o => o.ClassName == "lpsLyricMarker").ToList();
        Assert.True(lyrics.Count > 100,
            $"Erwartet viele Lyric-Marker, gefunden: {lyrics.Count}");
    }

    [Fact]
    public void Main_FindsHitMarkers()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var hits = deser.Objects.Where(o => o.ClassName == "lpsHitMarker").ToList();
        Assert.True(hits.Count > 100,
            $"Erwartet viele Hit-Marker, gefunden: {hits.Count}");
    }

    [Fact]
    public void Main_FindsTempoCode()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var tempos = deser.Objects.Where(o => o.ClassName == "ixSeqTempoCode").ToList();
        Assert.NotEmpty(tempos);
    }

    [Fact]
    public void Main_FindsSequences()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var sequences = deser.Objects.Where(o => o.ClassName == "ixSequence").ToList();
        Assert.True(sequences.Count >= 2,
            $"Erwartet mindestens 2 Sequenzen, gefunden: {sequences.Count}");
    }

    // ── Haupt-X360: lpsChart Feld-Auflösung ─────────────────────

    [Fact]
    public void Main_LpsChart_HasCorrectName()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var chart = deser.FindAndReadObject("lpsChart");
        Assert.NotNull(chart);
        Assert.Equal("California Love", chart["m_strName"]);
    }

    [Fact]
    public void Main_LpsChart_HasNoiseMaker()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var chart = deser.FindAndReadObject("lpsChart");
        Assert.NotNull(chart);
        Assert.Equal("Set003", chart["m_strNoiseMaker"]);
    }

    [Fact]
    public void Main_LpsChart_HasAudioEffectPath()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var chart = deser.FindAndReadObject("lpsChart");
        Assert.NotNull(chart);
        Assert.Equal("Effect01.xml", chart["m_strAudioEffectPresetPath"]);
    }

    [Fact]
    public void Main_LpsChart_HasSequences()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var chart = deser.FindAndReadObject("lpsChart");
        Assert.NotNull(chart);

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);
        Assert.Equal(15, seqVec.Count);
    }

    [Fact]
    public void Main_LpsChart_ReferenceCountIs1()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        var chart = deser.FindAndReadObject("lpsChart");
        Assert.NotNull(chart);
        Assert.Equal(1u, chart["m_uiReferenceCount"]);
    }

    // ── ReadAllObjects ──────────────────────────────────────────

    [Fact]
    public void Lyric_ReadAllObjects_ReturnsMultiple()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var deser = new IxbDeserializer(blob, header);

        var all = deser.ReadAllObjects();
        Assert.True(all.Count >= 3, $"Erwartet mindestens 3, gefunden: {all.Count}");

        // Jedes Objekt sollte ein __class-Feld haben
        foreach (var obj in all)
        {
            Assert.True(obj.ContainsKey("__class"), "Objekt ohne __class");
        }
    }

    // ── Vektor-Pointer-Auflösung ────────────────────────────────

    [Fact]
    public void Main_LpsChart_SequencesAreResolvedObjects()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);
        Assert.Equal(15, seqVec.Count);

        // Mindestens einige Elemente sollten aufgelöste Objekte sein (nicht nur Pointer-Strings)
        var resolvedCount = seqVec.Elements.Count(e => e is Dictionary<string, object?>);
        Assert.True(resolvedCount >= 10,
            $"Erwartet mindestens 10 aufgelöste Sequenzen, nur {resolvedCount} gefunden");
    }

    [Fact]
    public void Main_LpsChart_SequenceNamesCorrect()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var names = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .Select(e => e.TryGetValue("m_strName", out var n) ? n?.ToString() : null)
            .Where(n => n != null)
            .ToList();

        Assert.Contains("Melody", names);
        Assert.Contains("Lyric", names);
        Assert.Contains("Time", names);
        Assert.Contains("Conductor", names);
    }

    // ── Tempo-Map ───────────────────────────────────────────────

    [Fact]
    public void Main_TimeSequence_HasSeqCodes()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        // Finde die "Time"-Sequenz
        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var timeSeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Time");
        Assert.NotNull(timeSeq);

        // Die Time-Sequenz sollte SeqCode-Elemente haben
        var seqCodes = timeSeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);
        Assert.True(seqCodes.Count >= 1, "Time-Sequenz sollte mindestens 1 Code haben");

        // Die Elemente sollten aufgelöste Objekte sein mit Timing-Feldern
        var firstCode = seqCodes.Elements[0] as Dictionary<string, object?>;
        Assert.NotNull(firstCode);
        Assert.True(firstCode.ContainsKey("m_fTriggerTiming"), "SeqCode braucht TriggerTiming");
    }

    [Fact]
    public void Main_TempoCodeObject_ExistsInBlob()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);

        // Es sollte mindestens 1 ixSeqTempoCode-Objekt im Blob geben
        var tempoCodes = deser.Objects.Where(o => o.ClassName == "ixSeqTempoCode").ToList();
        Assert.True(tempoCodes.Count >= 1,
            $"Erwartet mindestens 1 ixSeqTempoCode im Blob, gefunden: {tempoCodes.Count}");

        // Lese das erste TempoCode-Objekt
        var cls = header.Classes.ClassList.First(c => c.Name == "ixSeqTempoCode");
        var obj = deser.ReadObject(cls, tempoCodes[0].BlobOffset);
        Assert.True(obj.ContainsKey("m_Tempo"), "TempoCode braucht m_Tempo-Feld");
    }

    // ── Melodie-Marker ──────────────────────────────────────────

    [Fact]
    public void Main_MelodySequence_ContainsMelodyMarkers()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var melodySeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Melody");
        Assert.NotNull(melodySeq);

        var seqCodes = melodySeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);
        Assert.True(seqCodes.Count > 50,
            $"Melody-Sequenz sollte viele Marker haben, nur {seqCodes.Count} gefunden");

        // Prüfe dass Marker Timing-Werte haben
        var firstMarker = seqCodes.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault();
        Assert.NotNull(firstMarker);
        Assert.True(firstMarker.ContainsKey("m_fTriggerTiming"), "Marker braucht TriggerTiming");
        Assert.True(firstMarker.ContainsKey("m_fLength"), "Marker braucht Length");
    }

    // ── Lyric-Marker ────────────────────────────────────────────

    [Fact]
    public void Main_LyricSequence_ContainsLyricMarkers()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var lyricSeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Lyric");
        Assert.NotNull(lyricSeq);

        var seqCodes = lyricSeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);
        Assert.True(seqCodes.Count > 100,
            $"Lyric-Sequenz sollte viele Marker haben, nur {seqCodes.Count} gefunden");
    }

    // ── Tone-Struct ────────────────────────────────────────────

    [Fact]
    public void Main_MelodyMarker_ToneHasFIdxAndOctave()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var melodySeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Melody");
        Assert.NotNull(melodySeq);

        var seqCodes = melodySeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);

        // Finde einen Marker mit m_Tone-Feld
        var markerWithTone = seqCodes.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_Tone", out var t) && t is Dictionary<string, object?>);
        Assert.NotNull(markerWithTone);

        var tone = markerWithTone["m_Tone"] as Dictionary<string, object?>;
        Assert.NotNull(tone);

        // Tone sollte als Struct mit fIdx und octave gelesen werden
        Assert.Equal("Tone", tone["__class"]);
        Assert.True(tone.ContainsKey("fIdx"), "Tone braucht fIdx-Feld");
        Assert.True(tone.ContainsKey("octave"), "Tone braucht octave-Feld");

        // fIdx sollte ein float sein
        Assert.IsType<float>(tone["fIdx"]);
        // octave sollte ein uint sein
        Assert.IsType<uint>(tone["octave"]);
    }

    [Fact]
    public void Main_MelodyMarker_ToneValuesArePlausible()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var melodySeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Melody");
        Assert.NotNull(melodySeq);

        var seqCodes = melodySeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);

        // Sammle alle Tone-Werte
        var tones = seqCodes.Elements
            .OfType<Dictionary<string, object?>>()
            .Where(e => e.TryGetValue("m_Tone", out var t) && t is Dictionary<string, object?>)
            .Select(e => e["m_Tone"] as Dictionary<string, object?>)
            .Where(t => t != null)
            .ToList();

        Assert.True(tones.Count > 10, $"Erwartet viele Tones, nur {tones.Count}");

        // fIdx sollte im Bereich 0-11 liegen (12 Halbtöne pro Oktave)
        foreach (var tone in tones)
        {
            var fIdx = (float)tone!["fIdx"];
            Assert.True(fIdx >= 0 && fIdx <= 11,
                $"fIdx={fIdx} sollte zwischen 0 und 11 liegen");

            var octave = (uint)tone["octave"];
            Assert.True(octave <= 10,
                $"octave={octave} sollte kleiner gleich 10 sein");
        }
    }

    // ── Klassen-Disambiguierung ─────────────────────────────────

    [Fact]
    public void Main_TimeSequence_SeqCodesAreNameTags()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var timeSeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Time");
        Assert.NotNull(timeSeq);

        var seqCodes = timeSeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);
        Assert.Equal(3, seqCodes.Count);

        // Alle 3 sollten ixSeqNameTag sein (Size=36, hat m_strTagName)
        var nameTags = seqCodes.Elements
            .OfType<Dictionary<string, object?>>()
            .Where(e => e.TryGetValue("__class", out var c) && c?.ToString() == "ixSeqNameTag")
            .ToList();

        Assert.Equal(3, nameTags.Count);
        Assert.Equal("Beat 8", nameTags[0]["m_strTagName"]);
        Assert.Equal("PV Start", nameTags[1]["m_strTagName"]);
        Assert.Equal("Stop", nameTags[2]["m_strTagName"]);
    }

    [Fact]
    public void Main_SequenceVectors_UseCorrectElementType()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        // m_vpSequence sollte den Element-Typ ixSequence* haben
        Assert.Contains("ixSequence", seqVec.ElementType);

        // Jede Sequenz sollte m_vpSeqCode mit Element-Typ ixSeqCode* haben
        var firstSeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .First();

        var seqCodeVec = firstSeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodeVec);
        Assert.Contains("ixSeqCode", seqCodeVec.ElementType);
    }

    // ── LyricMarker Felder ──────────────────────────────────────

    [Fact]
    public void Main_LyricMarker_HasFreeWordAndEndOfWord()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var lyricSeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Lyric");
        Assert.NotNull(lyricSeq);

        var seqCodes = lyricSeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);

        var firstLyric = seqCodes.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("__class", out var c) &&
                                 (c?.ToString() == "lpsLyricMarker" || c?.ToString() == "lpsHitMarker"));
        Assert.NotNull(firstLyric);
        Assert.True(firstLyric.ContainsKey("m_fTriggerTiming"));
        Assert.True(firstLyric.ContainsKey("m_bTriggered"));
    }

    [Fact]
    public void Main_LyricMarker_HasMelodyMarkerPointer()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var seqVec = chart["m_vpSequence"] as VectorInfo;
        Assert.NotNull(seqVec);

        var lyricSeq = seqVec.Elements
            .OfType<Dictionary<string, object?>>()
            .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Lyric");
        Assert.NotNull(lyricSeq);

        var seqCodes = lyricSeq["m_vpSeqCode"] as VectorInfo;
        Assert.NotNull(seqCodes);

        // Finde einen lpsLyricMarker mit m_pMelodyMarker
        var lyricMarkers = seqCodes.Elements
            .OfType<Dictionary<string, object?>>()
            .Where(e => e.ContainsKey("m_pMelodyMarker"))
            .ToList();

        Assert.True(lyricMarkers.Count > 0,
            "Mindestens ein Marker sollte m_pMelodyMarker haben");

        // m_pMelodyMarker sollte ein Pointer sein (String oder aufgelöstes Objekt)
        var melPtr = lyricMarkers[0]["m_pMelodyMarker"];
        Assert.NotNull(melPtr);
    }

    // ── Extra-Sequenzen ─────────────────────────────────────────

    [Fact]
    public void Main_ExtraSequences_AreResolved()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var deser = new IxbDeserializer(blob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        var extraVec = chart["m_vpExtraSequence"] as VectorInfo;
        Assert.NotNull(extraVec);
        Assert.Equal(6, extraVec.Count);

        var names = extraVec.Elements
            .OfType<Dictionary<string, object?>>()
            .Select(e => e.TryGetValue("m_strName", out var n) ? n?.ToString() : null)
            .Where(n => n != null)
            .ToList();

        Assert.Contains("Noisemaker_Mic", names);
    }
}
