using System.Globalization;
using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Serialisiert einen (ggf. im Editor bearbeiteten) UltraStarSong zurueck
/// ins UltraStar .txt-Format. Wichtig fuer den Editor-Flow: Aenderungen an
/// Noten/Lyrics/Preview fliessen so in die DLC-Pipeline ein.
/// </summary>
public static class UltraStarWriter
{
    public static string Serialize(UltraStarSong song)
    {
        var sb = new StringBuilder();

        void Tag(string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
                sb.AppendLine($"#{name}:{value}");
        }

        Tag("TITLE", song.Title);
        Tag("ARTIST", song.Artist);
        Tag("MP3", song.AudioFile);
        Tag("VIDEO", song.VideoFile);
        Tag("COVER", song.CoverFile);
        Tag("GENRE", song.Genre);
        Tag("YEAR", song.Year);
        Tag("LANGUAGE", song.Language);
        Tag("EDITION", song.Edition);
        sb.AppendLine($"#BPM:{song.Bpm.ToString("0.##", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"#GAP:{song.GapMs.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (song.PreviewStartSeconds > 0)
            sb.AppendLine($"#PREVIEWSTART:{song.PreviewStartSeconds.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (song.VideoGapSeconds != 0)
            sb.AppendLine($"#VIDEOGAP:{song.VideoGapSeconds.ToString("0.##", CultureInfo.InvariantCulture)}");

        // Noten: Original-Reihenfolge; P-Marker nur bei Duett-Songs
        var hasP2 = song.Notes.Any(n => n.Player == 2);
        var currentPlayer = 0;

        foreach (var note in song.Notes)
        {
            if (hasP2 && note.Player != currentPlayer)
            {
                currentPlayer = note.Player;
                sb.AppendLine($"P{currentPlayer}");
            }

            if (note.Type == UltraNoteType.PhraseBreak)
            {
                sb.AppendLine($"- {note.StartBeat}");
                continue;
            }

            var typeChar = note.Type switch
            {
                UltraNoteType.Golden => '*',
                UltraNoteType.Freestyle => 'F',
                _ => ':'
            };

            // Text NICHT trimmen - trailing Spaces markieren Wortgrenzen!
            sb.AppendLine($"{typeChar} {note.StartBeat} {note.Length} {note.Pitch} {note.Text}");
        }

        sb.AppendLine("E");
        return sb.ToString();
    }
}
