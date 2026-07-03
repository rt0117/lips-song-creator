namespace LipsSongExtractor.Tests;

public class LipsSongPackageBuilderTests
{
    private static UltraStarSong SampleUltraStarSong => UltraStarParser.Parse(@"#TITLE:Test Song
#ARTIST:Test Artist
#BPM:400
#GAP:5000
#GENRE:Pop
#YEAR:2024
#LANGUAGE:EN
: 0 5 0 Hel
: 5 3 2 lo
- 10
: 10 4 5 World
- 20
E
");

    // ── DLC.xml ─────────────────────────────────────────────────

    [Fact]
    public void DlcXml_ContainsAllRequiredTags()
    {
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = "Test Song",
            Artist = "Test Artist",
            Genre = "Rock",
            Year = "2023",
            Language = "EN",
            Album = "Test Album",
            LengthSeconds = 200
        };

        var xmlBytes = LipsSongPackageBuilder.BuildDlcXml(input, "Test Song");
        var xml = System.Text.Encoding.UTF8.GetString(xmlBytes);

        Assert.Contains("<DLCContents>", xml);
        Assert.Contains("<MusicIndex>", xml);
        Assert.Contains("<Artist>Test Artist</Artist>", xml);
        Assert.Contains("<Title>Test Song</Title>", xml);
        Assert.Contains("<Genre>Rock</Genre>", xml);
        Assert.Contains("<Year>2023</Year>", xml);
        Assert.Contains("<Language>EN</Language>", xml);
        Assert.Contains("<Album>Test Album</Album>", xml);
        Assert.Contains("<Length>200</Length>", xml);
        Assert.Contains("<ChartUri>Test Song.X360</ChartUri>", xml);
        Assert.Contains("<AudioUri>Test Song.xWMA</AudioUri>", xml);
        Assert.Contains("<LyricUri>Test Song_Lyric.X360</LyricUri>", xml);
        Assert.Contains("<AlbumJacketUri>Test Song.jpg</AlbumJacketUri>", xml);
        Assert.Contains("<offerID>", xml);
        Assert.Contains("<UintID>", xml);
        Assert.Contains("<ChartContentID>4D530888", xml);
        Assert.Contains("<LicenseBits", xml);
    }

    [Fact]
    public void DlcXml_EscapesSpecialCharacters()
    {
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = "Rock & Roll <Live>",
            Artist = "AC/DC & \"Friends\"",
        };

        var xmlBytes = LipsSongPackageBuilder.BuildDlcXml(input, "Rock and Roll");
        var xml = System.Text.Encoding.UTF8.GetString(xmlBytes);

        Assert.Contains("&amp;", xml);
        Assert.Contains("&lt;Live&gt;", xml);
        Assert.DoesNotContain("<Live>", xml);
    }

    [Fact]
    public void DlcXml_HasUniqueOfferId()
    {
        var input1 = new LipsSongPackageBuilder.SongInput { Title = "Song A", Artist = "Artist A" };
        var input2 = new LipsSongPackageBuilder.SongInput { Title = "Song B", Artist = "Artist B" };

        var xml1 = System.Text.Encoding.UTF8.GetString(LipsSongPackageBuilder.BuildDlcXml(input1, "Song A"));
        var xml2 = System.Text.Encoding.UTF8.GetString(LipsSongPackageBuilder.BuildDlcXml(input2, "Song B"));

        // Extrahiere offerIDs
        var offerId1 = ExtractTag(xml1, "offerID");
        var offerId2 = ExtractTag(xml2, "offerID");

        Assert.NotEqual(offerId1, offerId2);
    }

    // ── Lyric.X360 ──────────────────────────────────────────────

    [Fact]
    public void LyricX360_IsReadableByX360Reader()
    {
        var lyricBytes = LipsSongPackageBuilder.BuildLyricX360("Test Song", "Hello World\nThis is a test");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyric_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, lyricBytes);
            var (header, blob) = X360Reader.ReadFile(tempPath);

            Assert.True(header.IsBigEndian);
            Assert.True(blob.Length > 50);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void LyricX360_ContainsCorrectName()
    {
        var lyricBytes = LipsSongPackageBuilder.BuildLyricX360("My Song", "Lyrics here");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyric_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, lyricBytes);
            var (header, blob) = X360Reader.ReadFile(tempPath);
            var deser = new IxbDeserializer(blob, header);
            var rawFile = deser.FindAndReadObject("ixRawFileImage");

            Assert.NotNull(rawFile);
            Assert.Equal("My Song_Lyric", rawFile["m_strName"]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void LyricX360_ContainsLyricText()
    {
        var lyricText = "Hello World\nThis is a test\nLine three";
        var lyricBytes = LipsSongPackageBuilder.BuildLyricX360("Test", lyricText);

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyric_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, lyricBytes);
            var (header, blob) = X360Reader.ReadFile(tempPath);
            var deser = new IxbDeserializer(blob, header);
            var rawFile = deser.FindAndReadObject("ixRawFileImage");

            Assert.NotNull(rawFile);
            var vData = rawFile["m_vData"] as string;
            Assert.NotNull(vData);
            Assert.Contains("Hello World", vData);
            Assert.Contains("Line three", vData);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void LyricX360_HasTypeName_Text()
    {
        var lyricBytes = LipsSongPackageBuilder.BuildLyricX360("Test", "Lyrics");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyric_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, lyricBytes);
            var (header, blob) = X360Reader.ReadFile(tempPath);
            var deser = new IxbDeserializer(blob, header);
            var rawFile = deser.FindAndReadObject("ixRawFileImage");

            Assert.NotNull(rawFile);
            Assert.Equal("Text", rawFile["m_strTypeName"]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // ── Komplett-Paket ──────────────────────────────────────────

    [Fact]
    public void Build_WithUltraStar_ProducesAllFiles()
    {
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = "Test Song",
            Artist = "Test Artist",
            UltraStarSong = SampleUltraStarSong
        };

        var pkg = LipsSongPackageBuilder.Build(input);

        Assert.True(pkg.Files.ContainsKey("Test Song.X360"), "Chart fehlt");
        Assert.True(pkg.Files.ContainsKey("Test Song_Lyric.X360"), "Lyric fehlt");
        Assert.True(pkg.Files.ContainsKey("DLC.xml"), "DLC.xml fehlt");
        Assert.Equal(3, pkg.Files.Count);
    }

    [Fact]
    public void Build_ChartIsReadable()
    {
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = "Test Song",
            Artist = "Test Artist",
            UltraStarSong = SampleUltraStarSong
        };

        var pkg = LipsSongPackageBuilder.Build(input);
        var chartBytes = pkg.Files["Test Song.X360"];

        var tempPath = Path.Combine(Path.GetTempPath(), $"pkg_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, chartBytes);
            var (header, blob) = X360Reader.ReadFile(tempPath);
            var deser = new IxbDeserializer(blob, header);
            var chart = deser.FindAndReadObject("lpsChart");

            Assert.NotNull(chart);
            Assert.Equal("Test Song", chart["m_strName"]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Build_LyricTextFromUltraStar()
    {
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = "Test Song",
            Artist = "Test Artist",
            UltraStarSong = SampleUltraStarSong
        };

        var pkg = LipsSongPackageBuilder.Build(input);
        var lyricBytes = pkg.Files["Test Song_Lyric.X360"];

        var tempPath = Path.Combine(Path.GetTempPath(), $"pkg_lyric_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, lyricBytes);
            var (header, blob) = X360Reader.ReadFile(tempPath);
            var deser = new IxbDeserializer(blob, header);
            var rawFile = deser.FindAndReadObject("ixRawFileImage");

            Assert.NotNull(rawFile);
            var text = rawFile["m_vData"] as string;
            Assert.NotNull(text);
            Assert.Contains("Hel", text); // Silben zusammengesetzt
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Build_DlcXml_ReferencesCorrectFiles()
    {
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = "My Song",
            Artist = "My Artist",
            UltraStarSong = SampleUltraStarSong
        };

        var pkg = LipsSongPackageBuilder.Build(input);
        var xml = System.Text.Encoding.UTF8.GetString(pkg.Files["DLC.xml"]);

        Assert.Contains("<ChartUri>My Song.X360</ChartUri>", xml);
        Assert.Contains("<LyricUri>My Song_Lyric.X360</LyricUri>", xml);
    }

    private static string ExtractTag(string xml, string tag)
    {
        var start = xml.IndexOf($"<{tag}>") + tag.Length + 2;
        var end = xml.IndexOf($"</{tag}>");
        return xml[start..end];
    }
}
