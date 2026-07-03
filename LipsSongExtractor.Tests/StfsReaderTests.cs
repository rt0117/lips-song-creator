namespace LipsSongExtractor.Tests;

public class StfsReaderTests
{
    private static string DlcPackagePath => Path.Combine(
        TestHelpers.ExampleDir, "DLC", "4D530888", "00000002",
        "B4D42DF4F18ADE1D3A700ED04235CB8D1137D8254D");

    private static bool DlcAvailable => File.Exists(DlcPackagePath);

    [Fact]
    public void Read_Header_MagicIsLive()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        Assert.Equal("LIVE", stfs.Magic);
    }

    [Fact]
    public void Read_Header_TitleIdIsLips()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        Assert.Equal(0x4D530888u, stfs.TitleId);
    }

    [Fact]
    public void Read_Header_ContentTypeIsDlc()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        // 0x00000002 = Marketplace Content (DLC)
        Assert.Equal(0x00000002u, stfs.ContentType);
    }

    [Fact]
    public void Read_Header_DisplayName()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        Assert.Contains("From Yesterday", stfs.DisplayName);
    }

    [Fact]
    public void Read_FileTable_Contains9Files()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        Assert.Equal(9, stfs.Files.Count);
    }

    [Fact]
    public void Read_FileTable_ContainsExpectedFiles()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        var names = stfs.Files.Select(f => f.Name).ToHashSet();

        Assert.Contains("DLC.xml", names);
        Assert.Contains("From Yesterday.X360", names);
        Assert.Contains("From Yesterday.xWMA", names);
        Assert.Contains("From Yesterday_Lyric.X360", names);
        Assert.Contains("From Yesterday.jpg", names);
        Assert.Contains("From Yesterday_prv.nft", names);
    }

    [Fact]
    public void Extract_DlcXml_IsValidXml()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        var data = stfs.ExtractFile("DLC.xml");

        Assert.NotNull(data);
        var xml = System.Text.Encoding.UTF8.GetString(data);
        Assert.Contains("<DLCContents>", xml);
        Assert.Contains("<MusicIndex>", xml);
        Assert.Contains("From Yesterday", xml);
        Assert.Contains("<ChartUri>", xml);
    }

    [Fact]
    public void Extract_ChartX360_IsReadableByX360Reader()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        var data = stfs.ExtractFile("From Yesterday.X360");
        Assert.NotNull(data);

        // In Temp-Datei schreiben und mit unserem Reader lesen
        var tempPath = Path.Combine(Path.GetTempPath(), $"stfs_test_{Guid.NewGuid()}.x360");
        try
        {
            File.WriteAllBytes(tempPath, data);
            var (header, blob) = X360Reader.ReadFile(tempPath);

            Assert.True(header.IsBigEndian);
            Assert.True(blob.Length > 100_000);

            // Chart auslesen
            var deser = new IxbDeserializer(blob, header);
            var chart = deser.FindAndReadObject("lpsChart");
            Assert.NotNull(chart);
            Assert.Equal("From Yesterday", chart["m_strName"]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Extract_FileSize_MatchesEntry()
    {
        if (!DlcAvailable) return;
        using var stfs = new StfsReader(DlcPackagePath);
        var entry = stfs.Files.First(f => f.Name == "DLC.xml");
        var data = stfs.ExtractFile(entry);

        Assert.Equal((int)entry.Size, data.Length);
    }
}
