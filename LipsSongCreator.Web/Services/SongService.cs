using LipsSongExtractor;
using LipsSongExtractor.Poco;

namespace LipsSongCreator.Web.Services;

/// <summary>
/// Verwaltet den aktuell geladenen Song im Speicher.
/// Bietet typisierte Zugriffsmethoden fuer die UI.
/// </summary>
public class SongService
{
    private byte[]? _headerBytes;
    private byte[]? _blobBytes;
    private byte[]? _trailerBytes;
    private Ixb? _header;
    private IxbDeserializer? _deserializer;
    private IxbSerializer? _serializer;
    private string? _fileName;

    // UltraStar-Modus: Song aus TXT geladen (ohne .X360 Backend)
    private UltraStarSong? _ultraStarSong;
    private SongMetadata? _ultraStarMeta;
    private List<SequenceInfo>? _ultraStarSequences;

    public bool IsLoaded => (_header != null && _blobBytes != null) || _ultraStarSong != null;
    public bool IsUltraStarMode => _ultraStarSong != null;
    public string? FileName => _fileName;

    /// <summary>
    /// Entlaedt den aktuellen Song komplett (zurueck zur Landing Page).
    /// </summary>
    public void Unload()
    {
        _headerBytes = null;
        _blobBytes = null;
        _trailerBytes = null;
        _header = null;
        _deserializer = null;
        _serializer = null;
        _fileName = null;
        _ultraStarSong = null;
        _ultraStarMeta = null;
        _ultraStarSequences = null;
    }

    /// <summary>
    /// Laedt eine .X360-Datei aus einem Byte-Array.
    /// </summary>
    public void Load(string fileName, byte[] fileBytes)
    {
        // Schreibe temporaer auf Disk, da X360Reader/Writer Dateipfade erwarten
        var tempPath = Path.Combine(Path.GetTempPath(), $"lips_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, fileBytes);

            var (headerBytes, blob, trailer) = X360Writer.ReadRawParts(tempPath);
            var (header, _) = X360Reader.ReadFile(tempPath);

            _headerBytes = headerBytes;
            _blobBytes = blob;
            _trailerBytes = trailer;
            _header = header;
            _deserializer = new IxbDeserializer(blob, header);
            _serializer = new IxbSerializer(blob, header);
            _fileName = fileName;
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Gibt den modifizierten Song als .X360 Byte-Array zurueck.
    /// Im UltraStar-Modus wird der Song frisch aus den geparsten Daten generiert.
    /// </summary>
    public byte[] Export()
    {
        if (_ultraStarSong != null)
            return ExportUltraStarAsX360();

        if (_headerBytes == null || _serializer == null || _trailerBytes == null)
            throw new InvalidOperationException("Kein Song geladen.");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lips_export_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFileRaw(tempPath, _headerBytes, _serializer.GetBlob(), _trailerBytes);
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Generiert ein komplettes Song-Paket (ZIP) aus dem geladenen UltraStar-Song.
    /// </summary>
    public byte[] ExportAsZip()
    {
        if (_ultraStarSong == null)
            throw new InvalidOperationException("Nur im UltraStar-Modus verfuegbar.");

        var meta = _ultraStarMeta!;
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = meta.Name,
            Artist = meta.Artist,
            Album = meta.Album,
            Genre = meta.Genre.Length > 0 ? meta.Genre : "Pop",
            Year = meta.Year.Length > 0 ? meta.Year : "2024",
            Language = meta.Language.Length > 0 ? meta.Language : "EN",
            LengthSeconds = (int)_ultraStarSong.DurationSeconds,
            UltraStarSong = _ultraStarSong,
        };

        var pkg = LipsSongPackageBuilder.Build(input);

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var (name, data) in pkg.Files)
            {
                var entry = zip.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(data);
            }

            // Readme mit Anleitung
            var readmeEntry = zip.CreateEntry("README.txt");
            using var readmeStream = new StreamWriter(readmeEntry.Open());
            readmeStream.Write($@"Lips Song Creator - Generiertes Song-Paket
============================================
Titel: {input.Title}
Artist: {input.Artist}

Enthaltene Dateien:
  {input.Title}.X360            - Chart (Noten, Lyrics, Timing)
  {input.Title}_Lyric.X360      - Liedtext
  DLC.xml                       - Song-Index fuer das Spiel

Fehlende Dateien (manuell hinzufuegen):
  {input.Title}.xWMA            - Audio (xWMA-Format, konvertieren mit xWMAEncode)
  {input.Title}_prv.xWMA        - Audio-Preview (15-30 Sekunden Ausschnitt)
  {input.Title}.jpg             - Album-Cover (quadratisch, max 512x512)

Audio konvertieren:
  1. WAV-Datei vorbereiten (44100 Hz, 16-bit, Stereo)
  2. xWMAEncode.exe aus dem DirectX SDK verwenden:
     xWMAEncode ""{input.Title}.wav"" ""{input.Title}.xWMA""
  3. Fuer Preview: Ausschnitt zuschneiden, dann konvertieren

Auf die Xbox laden:
  1. Alle Dateien mit Velocity/Horizon in ein LIVE-Paket verpacken
     - Content Type: Marketplace Content (0x00000002)
     - Title ID: 4D530888 (Lips)
  2. Paket auf USB-Stick oder HDD kopieren nach:
     Content/0000000000000000/4D530888/00000002/
  3. Lips starten - der Song sollte im DLC-Bereich erscheinen
");
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Generiert ein STFS LIVE-Paket (.bin) das direkt auf die Xbox kopiert werden kann.
    /// Enthaelt DLC.xml + Chart + Lyric (ohne Audio/Cover - die muessen separat dazu).
    /// </summary>
    public byte[] ExportAsStfs()
    {
        if (_ultraStarSong == null)
            throw new InvalidOperationException("Nur im UltraStar-Modus verfuegbar.");

        var meta = _ultraStarMeta!;
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = meta.Name,
            Artist = meta.Artist,
            Album = meta.Album,
            Genre = meta.Genre.Length > 0 ? meta.Genre : "Pop",
            Year = meta.Year.Length > 0 ? meta.Year : "2024",
            Language = meta.Language.Length > 0 ? meta.Language : "EN",
            LengthSeconds = (int)_ultraStarSong.DurationSeconds,
            UltraStarSong = _ultraStarSong,
        };

        var pkg = LipsSongPackageBuilder.Build(input);

        return StfsWriter.CreatePackage(
            pkg.Files,
            $"\"{meta.Name}\"",
            $"Custom Song: {meta.Artist} - {meta.Name}",
            titleId: 0x4D530888,
            contentType: 0x00000002);
    }

    /// <summary>
    /// Generiert eine einzelne .X360-Datei aus dem geladenen UltraStar-Song.
    /// </summary>
    private byte[] ExportUltraStarAsX360()
    {
        var (headerBytes, blob) = UltraStarToLipsConverter.Convert(_ultraStarSong!);

        var tempPath = Path.Combine(Path.GetTempPath(), $"lips_us_export_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFile(tempPath, headerBytes, blob);
            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Laedt einen UltraStar TXT-Song und konvertiert ihn in das interne Format.
    /// </summary>
    public void LoadUltraStar(string fileName, string content)
    {
        _ultraStarSong = UltraStarParser.Parse(content);
        _fileName = fileName;

        _ultraStarMeta = new SongMetadata
        {
            Name = _ultraStarSong.Title,
            Artist = _ultraStarSong.Artist,
            Album = _ultraStarSong.Edition.Length > 0 ? _ultraStarSong.Edition : "",
            Genre = _ultraStarSong.Genre.Length > 0 ? _ultraStarSong.Genre : "Pop",
            Year = _ultraStarSong.Year.Length > 0 ? _ultraStarSong.Year : "2024",
            Language = _ultraStarSong.Language.Length > 0 ? _ultraStarSong.Language : "EN",
            NoiseMaker = "Set003",
            AudioEffectPath = "Effect01.xml",
            BaseCentOffset = 0,
            MusicStartOffset = 0,
        };

        _ultraStarSequences = ConvertUltraStarToSequences(_ultraStarSong);

        // Loesche evtl. vorherige X360-Daten
        _headerBytes = null;
        _blobBytes = null;
        _trailerBytes = null;
        _header = null;
        _deserializer = null;
        _serializer = null;
    }

    private static List<SequenceInfo> ConvertUltraStarToSequences(UltraStarSong us)
    {
        var sequences = new List<SequenceInfo>();

        // Melody-Sequenz: alle singbaren Noten mit Tonhoehe
        var melodySeq = new SequenceInfo { Name = "Melody", ClassName = "ixSequence" };
        foreach (var note in us.SingableNotes.Where(n => n.Player == 1))
        {
            melodySeq.Markers.Add(new MarkerInfo
            {
                ClassName = note.Type == UltraNoteType.Golden ? "lpsGoldenMarker" :
                            note.Type == UltraNoteType.Freestyle ? "lpsFreestyleMarker" :
                            "lpsMelodyMarker",
                TriggerTiming = us.BeatToSeconds(note.StartBeat),
                Length = us.BeatsToSeconds(note.Length),
                ToneFIdx = note.LipsFIdx,
                ToneOctave = note.LipsOctave,
                FreeWord = note.Text,
            });
        }
        melodySeq.MarkerCount = melodySeq.Markers.Count;
        sequences.Add(melodySeq);

        // Lyric-Sequenz: gleiche Noten, aber als Lyric-Marker
        var lyricSeq = new SequenceInfo { Name = "Lyric", ClassName = "ixSequence" };
        var notesList = us.SingableNotes.Where(n => n.Player == 1).ToList();
        for (var i = 0; i < notesList.Count; i++)
        {
            var note = notesList[i];
            var isEndOfWord = note.Text.EndsWith(" ") || note.Text.EndsWith("-") ||
                              i + 1 >= notesList.Count;

            lyricSeq.Markers.Add(new MarkerInfo
            {
                ClassName = "lpsLyricMarker",
                TriggerTiming = us.BeatToSeconds(note.StartBeat),
                Length = us.BeatsToSeconds(note.Length),
                FreeWord = note.Text.TrimEnd(),
                EndOfWord = isEndOfWord,
            });
        }
        lyricSeq.MarkerCount = lyricSeq.Markers.Count;
        sequences.Add(lyricSeq);

        // Time-Sequenz: BPM-Info als NameTags
        var timeSeq = new SequenceInfo { Name = "Time", ClassName = "ixTempoMap" };
        timeSeq.Markers.Add(new MarkerInfo
        {
            ClassName = "ixSeqNameTag",
            TriggerTiming = 0,
            Length = 0.1f,
            TagName = $"BPM {us.RealBpm:F0}",
        });
        timeSeq.MarkerCount = timeSeq.Markers.Count;
        sequences.Add(timeSeq);

        // Phrase-Sequenz: PhraseBreaks
        var sectionSeq = new SequenceInfo { Name = "Section", ClassName = "ixSequence" };
        foreach (var pb in us.Notes.Where(n => n.Type == UltraNoteType.PhraseBreak && n.Player == 1))
        {
            sectionSeq.Markers.Add(new MarkerInfo
            {
                ClassName = "lpsPhraseMarker",
                TriggerTiming = us.BeatToSeconds(pb.StartBeat),
                Length = 0.1f,
            });
        }
        sectionSeq.MarkerCount = sectionSeq.Markers.Count;
        sequences.Add(sectionSeq);

        return sequences;
    }

    /// <summary>
    /// Liest die Song-Metadaten (Name, NoiseMaker, etc.) aus dem lpsChart.
    /// </summary>
    public SongMetadata? GetMetadata()
    {
        if (_ultraStarSong != null) return _ultraStarMeta;

        if (_deserializer == null) return null;

        var chart = _deserializer.FindAndReadObject("lpsChart");
        if (chart == null) return null;

        return new SongMetadata
        {
            Name = chart.TryGetValue("m_strName", out var n) ? n?.ToString() ?? "" : "",
            NoiseMaker = chart.TryGetValue("m_strNoiseMaker", out var nm) ? nm?.ToString() ?? "" : "",
            AudioEffectPath = chart.TryGetValue("m_strAudioEffectPresetPath", out var ae)
                ? ae?.ToString() ?? ""
                : "",
            BaseCentOffset = chart.TryGetValue("m_BaseCentOffset", out var bco) ? Convert.ToInt32(bco) : 0,
            MusicStartOffset = chart.TryGetValue("m_MusicStartOffset", out var mso)
                ? Convert.ToInt32(mso)
                : 0,
        };
    }

    /// <summary>
    /// Liest alle Sequenzen mit deren SeqCodes aus dem Chart.
    /// </summary>
    public List<SequenceInfo> GetSequences()
    {
        if (_ultraStarSequences != null) return _ultraStarSequences;

        if (_deserializer == null) return [];

        var chart = _deserializer.FindAndReadObject("lpsChart");
        if (chart == null) return [];

        var result = new List<SequenceInfo>();

        if (chart.TryGetValue("m_vpSequence", out var seqObj) && seqObj is VectorInfo seqVec)
        {
            foreach (var elem in seqVec.Elements.OfType<Dictionary<string, object?>>())
            {
                result.Add(ExtractSequenceInfo(elem));
            }
        }

        if (chart.TryGetValue("m_vpExtraSequence", out var extraObj) && extraObj is VectorInfo extraVec)
        {
            foreach (var elem in extraVec.Elements.OfType<Dictionary<string, object?>>())
            {
                var info = ExtractSequenceInfo(elem);
                info.IsExtra = true;
                result.Add(info);
            }
        }

        return result;
    }

    /// <summary>
    /// Aendert ein String-Feld im Song.
    /// </summary>
    public bool SetString(string className, string fieldName, string value)
    {
        return _serializer?.SetString(className, fieldName, value) ?? false;
    }

    /// <summary>
    /// Aendert ein numerisches Feld im Song.
    /// </summary>
    public bool SetField(string className, string fieldName, object value)
    {
        return _serializer?.SetField(className, fieldName, value) ?? false;
    }

    private static SequenceInfo ExtractSequenceInfo(Dictionary<string, object?> seqDict)
    {
        var info = new SequenceInfo
        {
            Name = seqDict.TryGetValue("m_strName", out var n) ? n?.ToString() ?? "" : "",
            ClassName = seqDict.TryGetValue("__class", out var c) ? c?.ToString() ?? "" : "",
        };

        if (seqDict.TryGetValue("m_vpSeqCode", out var codeObj) && codeObj is VectorInfo codeVec)
        {
            info.MarkerCount = codeVec.Count;

            foreach (var marker in codeVec.Elements.OfType<Dictionary<string, object?>>())
            {
                var mi = new MarkerInfo
                {
                    ClassName = marker.TryGetValue("__class", out var mc) ? mc?.ToString() ?? "" : "",
                };

                if (marker.TryGetValue("m_fTriggerTiming", out var tt) && tt is float ftVal)
                    mi.TriggerTiming = ftVal;
                else if (marker.TryGetValue("m_fTriggerTiming", out var tt2))
                    mi.TriggerTiming = Convert.ToSingle(tt2);

                if (marker.TryGetValue("m_fLength", out var fl) && fl is float flVal)
                    mi.Length = flVal;

                if (marker.TryGetValue("m_strFreeWord", out var fw))
                    mi.FreeWord = fw?.ToString() ?? "";

                if (marker.TryGetValue("m_bEndOfWord", out var eow))
                    mi.EndOfWord = Convert.ToInt32(eow) != 0;

                if (marker.TryGetValue("m_strTagName", out var tn))
                    mi.TagName = tn?.ToString() ?? "";

                // Tone
                if (marker.TryGetValue("m_Tone", out var toneObj) &&
                    toneObj is Dictionary<string, object?> tone)
                {
                    if (tone.TryGetValue("fIdx", out var fi) && fi is float fiVal)
                        mi.ToneFIdx = fiVal;
                    if (tone.TryGetValue("octave", out var oct))
                        mi.ToneOctave = Convert.ToInt32(oct);
                }

                info.Markers.Add(mi);
            }
        }

        return info;
    }
}

public class SongMetadata
{
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "Pop";
    public string Year { get; set; } = "2024";
    public string Language { get; set; } = "EN";
    public string NoiseMaker { get; set; } = "";
    public string AudioEffectPath { get; set; } = "";
    public int BaseCentOffset { get; set; }
    public int MusicStartOffset { get; set; }
}

public class SequenceInfo
{
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public int MarkerCount { get; set; }
    public bool IsExtra { get; set; }
    public List<MarkerInfo> Markers { get; set; } = [];
}

public class MarkerInfo
{
    public string ClassName { get; set; } = "";
    public float TriggerTiming { get; set; }
    public float Length { get; set; }
    public string? FreeWord { get; set; }
    public bool EndOfWord { get; set; }
    public string? TagName { get; set; }
    public float? ToneFIdx { get; set; }
    public int? ToneOctave { get; set; }

    /// <summary>
    /// Formatiert das Timing als MM:SS.ms
    /// </summary>
    public string TimingFormatted
    {
        get
        {
            var ts = TimeSpan.FromSeconds(TriggerTiming);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }
    }

    /// <summary>
    /// Gibt den Notennamen zurueck (C, C#, D, ..., B)
    /// </summary>
    public string? NoteName
    {
        get
        {
            if (ToneFIdx == null || ToneOctave == null) return null;
            var names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var idx = (int)ToneFIdx.Value;
            if (idx < 0 || idx >= names.Length) return $"?{idx}";
            return $"{names[idx]}{ToneOctave}";
        }
    }
}
