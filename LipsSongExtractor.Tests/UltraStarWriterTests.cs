namespace LipsSongExtractor.Tests;

public class UltraStarWriterTests
{
    private const string SampleSong = @"#TITLE:Test Song
#ARTIST:Test Artist
#MP3:test.mp3
#BPM:400
#GAP:5000
#PREVIEWSTART:42.5
: 0 5 0 Cal
: 5 3 2 i
: 8 4 4 for 
* 12 6 7 nia 
- 20
: 20 4 5 knows 
F 32 8 0 par
: 40 4 2 ty
E
";

    [Fact]
    public void Roundtrip_PreservesNotes()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var serialized = UltraStarWriter.Serialize(song);
        var reparsed = UltraStarParser.Parse(serialized);

        Assert.Equal(song.Notes.Count, reparsed.Notes.Count);
        for (var i = 0; i < song.Notes.Count; i++)
        {
            Assert.Equal(song.Notes[i].Type, reparsed.Notes[i].Type);
            Assert.Equal(song.Notes[i].StartBeat, reparsed.Notes[i].StartBeat);
            Assert.Equal(song.Notes[i].Length, reparsed.Notes[i].Length);
            Assert.Equal(song.Notes[i].Pitch, reparsed.Notes[i].Pitch);
            Assert.Equal(song.Notes[i].Text, reparsed.Notes[i].Text);
        }
    }

    [Fact]
    public void Roundtrip_PreservesTrailingSpaces()
    {
        // Trailing Spaces markieren Wortgrenzen - MUESSEN erhalten bleiben
        var song = UltraStarParser.Parse(SampleSong);
        var serialized = UltraStarWriter.Serialize(song);
        var reparsed = UltraStarParser.Parse(serialized);

        var forNote = reparsed.SingableNotes.First(n => n.Text.StartsWith("for"));
        Assert.EndsWith(" ", forNote.Text);
    }

    [Fact]
    public void Roundtrip_PreservesHeader()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var serialized = UltraStarWriter.Serialize(song);
        var reparsed = UltraStarParser.Parse(serialized);

        Assert.Equal(song.Title, reparsed.Title);
        Assert.Equal(song.Artist, reparsed.Artist);
        Assert.Equal(song.Bpm, reparsed.Bpm);
        Assert.Equal(song.GapMs, reparsed.GapMs);
        Assert.Equal(song.PreviewStartSeconds, reparsed.PreviewStartSeconds);
    }

    [Fact]
    public void Serialize_ModifiedNote_ReflectsChanges()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var note = song.SingableNotes[0];
        note.StartBeat = 100;
        note.Length = 20;
        note.Pitch = 5;
        note.Text = "Changed ";

        var serialized = UltraStarWriter.Serialize(song);
        var reparsed = UltraStarParser.Parse(serialized);

        var first = reparsed.SingableNotes[0];
        Assert.Equal(100, first.StartBeat);
        Assert.Equal(20, first.Length);
        Assert.Equal(5, first.Pitch);
        Assert.Equal("Changed ", first.Text);
    }
}
