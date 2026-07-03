namespace LipsSongExtractor.Tests;

public class UltraStarToLipsConverterTests
{
    private const string SampleSong = @"#TITLE:Test Song
#ARTIST:Test Artist
#MP3:test.mp3
#BPM:400
#GAP:5000
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

    [Fact]
    public void Convert_ProducesNonEmptyHeaderAndBlob()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, blob) = UltraStarToLipsConverter.Convert(song);

        Assert.True(header.Length > 100, $"Header sollte nicht leer sein, ist {header.Length} Bytes");
        Assert.True(blob.Length > 100, $"Blob sollte nicht leer sein, ist {blob.Length} Bytes");
    }

    [Fact]
    public void Convert_HeaderContainsValidXml()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, _) = UltraStarToLipsConverter.Convert(song);
        var headerStr = System.Text.Encoding.UTF8.GetString(header);

        Assert.StartsWith("<ixb", headerStr);
        Assert.Contains("IsBigEndian=\"true\"", headerStr);
        Assert.Contains("<Classes>", headerStr);
        Assert.Contains("lpsChart", headerStr);
    }

    [Fact]
    public void Convert_CanBeWrittenAndReadBack()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, blob) = UltraStarToLipsConverter.Convert(song);

        var tempPath = Path.Combine(Path.GetTempPath(), $"ultrastar_convert_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFile(tempPath, header, blob);

            // Muss vom Reader gelesen werden koennen
            var (readHeader, readBlob) = X360Reader.ReadFile(tempPath);
            Assert.True(readHeader.IsBigEndian);
            Assert.Equal(blob.Length, readBlob.Length);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Convert_ChartHasCorrectTitle()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, blob) = UltraStarToLipsConverter.Convert(song);

        var tempPath = Path.Combine(Path.GetTempPath(), $"ultrastar_convert_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFile(tempPath, header, blob);
            var (readHeader, readBlob) = X360Reader.ReadFile(tempPath);

            var deser = new IxbDeserializer(readBlob, readHeader);
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
    public void Convert_ChartHasMelodySequenceWithNotes()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, blob) = UltraStarToLipsConverter.Convert(song);

        var tempPath = Path.Combine(Path.GetTempPath(), $"ultrastar_convert_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFile(tempPath, header, blob);
            var (readHeader, readBlob) = X360Reader.ReadFile(tempPath);

            var deser = new IxbDeserializer(readBlob, readHeader);
            var chart = deser.FindAndReadObject("lpsChart");
            Assert.NotNull(chart);

            var seqVec = chart["m_vpSequence"] as VectorInfo;
            Assert.NotNull(seqVec);
            Assert.True(seqVec.Count > 0, "Sollte Sequenzen enthalten");

            var melodySeq = seqVec.Elements
                .OfType<Dictionary<string, object?>>()
                .FirstOrDefault(e => e.TryGetValue("m_strName", out var n) && n?.ToString() == "Melody");

            Assert.NotNull(melodySeq);

            var seqCodes = melodySeq["m_vpSeqCode"] as VectorInfo;
            Assert.NotNull(seqCodes);
            Assert.Equal(9, seqCodes.Count); // 9 singbare Noten
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Convert_MelodyMarkerHasCorrectTiming()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, blob) = UltraStarToLipsConverter.Convert(song);

        var tempPath = Path.Combine(Path.GetTempPath(), $"ultrastar_convert_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFile(tempPath, header, blob);
            var (readHeader, readBlob) = X360Reader.ReadFile(tempPath);

            var deser = new IxbDeserializer(readBlob, readHeader);
            var chart = deser.FindAndReadObject("lpsChart")!;
            var seqVec = chart["m_vpSequence"] as VectorInfo;
            var melodySeq = seqVec!.Elements
                .OfType<Dictionary<string, object?>>()
                .First(e => e["m_strName"]?.ToString() == "Melody");

            var seqCodes = melodySeq["m_vpSeqCode"] as VectorInfo;
            var firstMarker = seqCodes!.Elements.OfType<Dictionary<string, object?>>().First();

            // Erste Note: Beat 0 -> GAP/1000 = 5.0s
            var timing = (float)firstMarker["m_fTriggerTiming"]!;
            Assert.Equal(5.0f, timing, 2);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Convert_LyricMarkerHasCorrectText()
    {
        var song = UltraStarParser.Parse(SampleSong);
        var (header, blob) = UltraStarToLipsConverter.Convert(song);

        var tempPath = Path.Combine(Path.GetTempPath(), $"ultrastar_convert_{Guid.NewGuid()}.x360");
        try
        {
            X360Writer.WriteFile(tempPath, header, blob);
            var (readHeader, readBlob) = X360Reader.ReadFile(tempPath);

            var deser = new IxbDeserializer(readBlob, readHeader);
            var chart = deser.FindAndReadObject("lpsChart")!;
            var seqVec = chart["m_vpSequence"] as VectorInfo;
            var lyricSeq = seqVec!.Elements
                .OfType<Dictionary<string, object?>>()
                .First(e => e["m_strName"]?.ToString() == "Lyric");

            var seqCodes = lyricSeq["m_vpSeqCode"] as VectorInfo;
            var firstMarker = seqCodes!.Elements.OfType<Dictionary<string, object?>>().First();

            Assert.Equal("Cal", firstMarker["m_strFreeWord"]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
