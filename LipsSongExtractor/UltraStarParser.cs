using System.Globalization;

namespace LipsSongExtractor;

/// <summary>
/// Parser fuer UltraStar TXT-Dateien.
/// 
/// Format-Referenz: https://usdx.eu/format/
/// 
/// Header: #TAG:VALUE (z.B. #TITLE:California Love, #BPM:400, #GAP:1234)
/// Noten:  Type StartBeat Length Pitch Text
///   : = Normal, * = Golden, F = Freestyle, R = Rap, G = GoldenRap
/// Phrasen-Ende: - StartBeat
/// Datei-Ende: E
/// 
/// Pitch 0 = C4 = MIDI 60
/// BPM ist UltraStar-BPM (typisch 4x reale BPM)
/// Beat zu Sekunden: time = GAP/1000 + beat * 60 / BPM
/// </summary>
public static class UltraStarParser
{
    public static UltraStarSong Parse(string content)
    {
        var song = new UltraStarSong();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var currentPlayer = 1;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim('\r', '\t', ' ');
            if (string.IsNullOrEmpty(line)) continue;

            // Header-Tags
            if (line.StartsWith('#'))
            {
                var colonIdx = line.IndexOf(':');
                if (colonIdx <= 1) continue;

                var tag = line[1..colonIdx].Trim().ToUpperInvariant();
                var value = line[(colonIdx + 1)..].Trim();

                switch (tag)
                {
                    case "TITLE": song.Title = value; break;
                    case "ARTIST": song.Artist = value; break;
                    case "MP3": song.AudioFile = value; break;
                    case "AUDIO": song.AudioFile = value; break;
                    case "COVER": song.CoverFile = value; break;
                    case "BACKGROUND": song.BackgroundFile = value; break;
                    case "VIDEO": song.VideoFile = value; break;
                    case "GENRE": song.Genre = value; break;
                    case "YEAR": song.Year = value; break;
                    case "LANGUAGE": song.Language = value; break;
                    case "EDITION": song.Edition = value; break;
                    case "CREATOR": song.Creator = value; break;
                    case "BPM":
                        song.Bpm = ParseFloat(value);
                        break;
                    case "GAP":
                        song.GapMs = ParseFloat(value);
                        break;
                    case "VIDEOGAP":
                        song.VideoGapSeconds = ParseFloat(value);
                        break;
                    case "PREVIEWSTART":
                        song.PreviewStartSeconds = ParseFloat(value);
                        break;
                    case "RELATIVE":
                        song.IsRelative = value == "YES" || value == "yes" || value == "1";
                        break;
                }

                continue;
            }

            // Player delimiter
            if (line.StartsWith("P1") || line.StartsWith("P 1"))
            {
                currentPlayer = 1;
                continue;
            }

            if (line.StartsWith("P2") || line.StartsWith("P 2"))
            {
                currentPlayer = 2;
                continue;
            }

            // End of file
            if (line == "E") break;

            // Phrase break: - StartBeat
            if (line.StartsWith('-'))
            {
                var parts = line[1..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1 && int.TryParse(parts[0], out var breakBeat))
                {
                    song.Notes.Add(new UltraStarNote
                    {
                        Type = UltraNoteType.PhraseBreak,
                        StartBeat = breakBeat,
                        Length = 0,
                        Pitch = 0,
                        Text = "",
                        Player = currentPlayer
                    });
                }

                continue;
            }

            // Note line: Type StartBeat Length Pitch Text
            var noteType = line[0] switch
            {
                ':' => UltraNoteType.Normal,
                '*' => UltraNoteType.Golden,
                'F' => UltraNoteType.Freestyle,
                'R' => UltraNoteType.Rap,
                'G' => UltraNoteType.GoldenRap,
                _ => (UltraNoteType?)null
            };

            if (noteType == null) continue;

            var noteParts = line[1..].Trim().Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
            if (noteParts.Length < 4) continue;

            if (!int.TryParse(noteParts[0], out var startBeat)) continue;
            if (!int.TryParse(noteParts[1], out var length)) continue;
            if (!int.TryParse(noteParts[2], out var pitch)) continue;

            var text = noteParts.Length >= 4 ? string.Join(" ", noteParts[3..]) : "";

            song.Notes.Add(new UltraStarNote
            {
                Type = noteType.Value,
                StartBeat = startBeat,
                Length = length,
                Pitch = pitch,
                Text = text,
                Player = currentPlayer
            });
        }

        return song;
    }

    public static UltraStarSong ParseFile(string path)
    {
        var content = File.ReadAllText(path);
        return Parse(content);
    }

    private static float ParseFloat(string s)
    {
        // UltraStar-Dateien verwenden sowohl Punkt als auch Komma als Dezimaltrenner
        s = s.Replace(',', '.');
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}

public class UltraStarSong
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string AudioFile { get; set; } = "";
    public string CoverFile { get; set; } = "";
    public string BackgroundFile { get; set; } = "";
    public string VideoFile { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Year { get; set; } = "";
    public string Language { get; set; } = "";
    public string Edition { get; set; } = "";
    public string Creator { get; set; } = "";
    public float Bpm { get; set; }
    public float GapMs { get; set; }
    public float VideoGapSeconds { get; set; }
    public float PreviewStartSeconds { get; set; }
    public bool IsRelative { get; set; }

    public List<UltraStarNote> Notes { get; set; } = [];

    /// <summary>
    /// Reale BPM des Songs (UltraStar BPM / 4).
    /// </summary>
    public float RealBpm => Bpm / 4f;

    /// <summary>
    /// Konvertiert einen Beat in Sekunden.
    /// UltraStar-Formel: Sekunden = GAP/1000 + Beat * 60 / (BPM * 4)
    /// (Beats sind Viertel-Beats; verifiziert gegen #END-Tags echter Songs).
    /// </summary>
    public float BeatToSeconds(int beat)
    {
        return GapMs / 1000f + beat * 60f / (Bpm * 4f);
    }

    /// <summary>
    /// Konvertiert eine Beat-Laenge in Sekunden.
    /// </summary>
    public float BeatsToSeconds(int beats)
    {
        return beats * 60f / (Bpm * 4f);
    }

    /// <summary>
    /// Gibt nur die singbaren Noten zurueck (ohne PhraseBreaks).
    /// </summary>
    public List<UltraStarNote> SingableNotes =>
        Notes.Where(n => n.Type != UltraNoteType.PhraseBreak).ToList();

    /// <summary>
    /// Gibt die Noten eines bestimmten Spielers zurueck.
    /// </summary>
    public List<UltraStarNote> GetPlayerNotes(int player) =>
        Notes.Where(n => n.Player == player).ToList();

    /// <summary>
    /// Song-Dauer in Sekunden (basierend auf dem letzten Beat).
    /// </summary>
    public float DurationSeconds
    {
        get
        {
            if (Notes.Count == 0) return 0;
            var lastNote = Notes.Where(n => n.Type != UltraNoteType.PhraseBreak).MaxBy(n => n.StartBeat + n.Length);
            if (lastNote == null) return 0;
            return BeatToSeconds(lastNote.StartBeat + lastNote.Length);
        }
    }
}

public class UltraStarNote
{
    public UltraNoteType Type { get; set; }
    public int StartBeat { get; set; }
    public int Length { get; set; }
    public int Pitch { get; set; }
    public string Text { get; set; } = "";
    public int Player { get; set; } = 1;

    /// <summary>
    /// MIDI-Notennummer (Pitch 0 = C4 = MIDI 60).
    /// </summary>
    public int MidiNote => Pitch + 60;

    /// <summary>
    /// Lips Tone: fIdx = Halbton innerhalb der Oktave (0-11).
    /// </summary>
    public float LipsFIdx => MidiNote % 12;

    /// <summary>
    /// Lips Tone: Oktave.
    /// </summary>
    public int LipsOctave => MidiNote / 12 - 1;

    /// <summary>
    /// Notennamen (C, C#, D, ..., B).
    /// </summary>
    public string NoteName
    {
        get
        {
            var names = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var idx = ((MidiNote % 12) + 12) % 12; // Handle negative pitches
            return $"{names[idx]}{LipsOctave}";
        }
    }
}

public enum UltraNoteType
{
    Normal,       // :
    Golden,       // *
    Freestyle,    // F
    Rap,          // R
    GoldenRap,    // G
    PhraseBreak   // -
}
