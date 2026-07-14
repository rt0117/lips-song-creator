namespace LipsSongExtractor.Tests;

public class UltraStarParserTests
{
    private const string SampleSong = @"#TITLE:Test Song
#ARTIST:Test Artist
#MP3:test.mp3
#BPM:400
#GAP:5000
#COVER:cover.jpg
#GENRE:Pop
#YEAR:2024
#LANGUAGE:English
: 0 5 0 Cal
: 5 3 2 i
: 8 4 4 for
* 12 6 7 nia
- 20
: 20 4 5 knows
: 24 3 2 how
: 27 5 0 to
F 32 8 0 par
: 40 4 2 ty
- 48
E
";

    // ── Header-Parsing ──────────────────────────────────────────

    [Fact]
    public void Parse_Title()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal("Test Song", song.Title);
    }

    [Fact]
    public void Parse_Artist()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal("Test Artist", song.Artist);
    }

    [Fact]
    public void Parse_AudioFile()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal("test.mp3", song.AudioFile);
    }

    [Fact]
    public void Parse_BPM()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal(400f, song.Bpm);
    }

    [Fact]
    public void Parse_RealBPM()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal(100f, song.RealBpm);
    }

    [Fact]
    public void Parse_Gap()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal(5000f, song.GapMs);
    }

    [Fact]
    public void Parse_OptionalFields()
    {
        var song = UltraStarParser.Parse(SampleSong);
        Assert.Equal("cover.jpg", song.CoverFile);
        Assert.Equal("Pop", song.Genre);
        Assert.Equal("2024", song.Year);
        Assert.Equal("English", song.Language);
    }

    // ── Noten-Parsing ───────────────────────────────────────────

    [Fact]
    public void Parse_NoteCount()
    {
        var song = UltraStarParser.Parse(SampleSong);
        // 9 singbare Noten + 2 PhraseBreaks = 11 total
        Assert.Equal(11, song.Notes.Count);
        Assert.Equal(9, song.SingableNotes.Count);
    }

    [Fact]
    public void Parse_FirstNote_IsNormal()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var note = song.SingableNotes[0];

        Assert.Equal(UltraNoteType.Normal, note.Type);
        Assert.Equal(0, note.StartBeat);
        Assert.Equal(5, note.Length);
        Assert.Equal(0, note.Pitch);
        Assert.Equal("Cal", note.Text);
    }

    [Fact]
    public void Parse_GoldenNote()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var golden = song.SingableNotes.First(n => n.Type == UltraNoteType.Golden);

        Assert.Equal(12, golden.StartBeat);
        Assert.Equal(6, golden.Length);
        Assert.Equal(7, golden.Pitch);
        Assert.Equal("nia", golden.Text);
    }

    [Fact]
    public void Parse_FreestyleNote()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var freestyle = song.SingableNotes.First(n => n.Type == UltraNoteType.Freestyle);

        Assert.Equal(32, freestyle.StartBeat);
        Assert.Equal(8, freestyle.Length);
        Assert.Equal("par", freestyle.Text);
    }

    [Fact]
    public void Parse_PhraseBreaks()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var breaks = song.Notes.Where(n => n.Type == UltraNoteType.PhraseBreak).ToList();

        Assert.Equal(2, breaks.Count);
        Assert.Equal(20, breaks[0].StartBeat);
        Assert.Equal(48, breaks[1].StartBeat);
    }

    // ── Timing-Konvertierung ────────────────────────────────────

    [Fact]
    public void BeatToSeconds_FirstNote()
    {
        var song = UltraStarParser.Parse(SampleSong);
        // Beat 0: GAP/1000 + 0 = 5.0s
        Assert.Equal(5.0f, song.BeatToSeconds(0), 3);
    }

    [Fact]
    public void BeatToSeconds_WithOffset()
    {
        var song = UltraStarParser.Parse(SampleSong);
        // UltraStar-Beats sind Viertel-Beats:
        // Beat 20: 5.0 + 20 * 60/(400*4) = 5.0 + 0.75 = 5.75s
        Assert.Equal(5.75f, song.BeatToSeconds(20), 3);
    }

    [Fact]
    public void BeatsToSeconds_Length()
    {
        var song = UltraStarParser.Parse(SampleSong);
        // 5 Beats: 5 * 60/(400*4) = 0.1875s
        Assert.Equal(0.1875f, song.BeatsToSeconds(5), 3);
    }

    // ── MIDI/Lips-Konvertierung ─────────────────────────────────

    [Fact]
    public void MidiNote_Pitch0_IsC4()
    {
        var note = new UltraStarNote { Pitch = 0 };
        Assert.Equal(60, note.MidiNote);
        Assert.Equal(0f, note.LipsFIdx); // C
        Assert.Equal(4, note.LipsOctave);
        Assert.Equal("C4", note.NoteName);
    }

    [Fact]
    public void MidiNote_Pitch7_IsG4()
    {
        var note = new UltraStarNote { Pitch = 7 };
        Assert.Equal(67, note.MidiNote);
        Assert.Equal(7f, note.LipsFIdx); // G
        Assert.Equal(4, note.LipsOctave);
        Assert.Equal("G4", note.NoteName);
    }

    [Fact]
    public void MidiNote_Pitch12_IsC5()
    {
        var note = new UltraStarNote { Pitch = 12 };
        Assert.Equal(72, note.MidiNote);
        Assert.Equal(0f, note.LipsFIdx); // C
        Assert.Equal(5, note.LipsOctave);
        Assert.Equal("C5", note.NoteName);
    }

    [Fact]
    public void MidiNote_NegativePitch_HandledCorrectly()
    {
        var note = new UltraStarNote { Pitch = -12 };
        Assert.Equal(48, note.MidiNote);
        Assert.Equal(0f, note.LipsFIdx); // C
        Assert.Equal(3, note.LipsOctave);
        Assert.Equal("C3", note.NoteName);
    }

    // ── Duration ────────────────────────────────────────────────

    [Fact]
    public void DurationSeconds_Calculated()
    {
        var song = UltraStarParser.Parse(SampleSong);
        // Last singable note: beat 40, length 4 -> end beat 44
        // Time = 5.0 + 44 * 60/(400*4) = 5.0 + 1.65 = 6.65s
        Assert.Equal(6.65f, song.DurationSeconds, 1);
    }

    // ── Edge Cases ──────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptySong()
    {
        var song = UltraStarParser.Parse("");
        Assert.Equal("", song.Title);
        Assert.Empty(song.Notes);
    }

    [Fact]
    public void Parse_OnlyHeaders_NoNotes()
    {
        var song = UltraStarParser.Parse("#TITLE:Only Headers\n#BPM:300\nE\n");
        Assert.Equal("Only Headers", song.Title);
        Assert.Equal(300f, song.Bpm);
        Assert.Empty(song.Notes);
    }

    [Fact]
    public void Parse_BpmWithComma()
    {
        // Manche UltraStar-Dateien verwenden Komma als Dezimaltrenner
        var song = UltraStarParser.Parse("#BPM:299,5\nE\n");
        Assert.Equal(299.5f, song.Bpm);
    }

    [Fact]
    public void Parse_AudioTag()
    {
        var song = UltraStarParser.Parse("#AUDIO:song.ogg\nE\n");
        Assert.Equal("song.ogg", song.AudioFile);
    }

    // ── Duett (P1/P2) ──────────────────────────────────────────

    [Fact]
    public void Parse_Duett_PlayerDelimiters()
    {
        var duett = @"#TITLE:Duett
#BPM:400
#GAP:0
P1
: 0 5 0 Player1
- 10
P2
: 10 5 5 Player2
- 20
E
";
        var song = UltraStarParser.Parse(duett);
        var p1 = song.GetPlayerNotes(1);
        var p2 = song.GetPlayerNotes(2);

        Assert.Equal(2, p1.Count); // 1 note + 1 break
        Assert.Equal(2, p2.Count);
        Assert.Equal("Player1", p1[0].Text);
        Assert.Equal("Player2", p2[0].Text);
    }
}
