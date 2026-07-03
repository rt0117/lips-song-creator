namespace LipsSongExtractor.Tests;

public class X360ReaderTests
{
    // ── ReadFile: Header-Parsing ────────────────────────────────

    [Fact]
    public void ReadFile_CaliforniaLove_ParsesHeader()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);

        Assert.True(header.IsBigEndian);
        Assert.Equal("WIN32", header.Platform);
        Assert.Equal(6815, header.NumOfElements);
    }

    [Fact]
    public void ReadFile_CaliforniaLove_BlobIsNonEmpty()
    {
        var (_, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);

        Assert.True(blob.Length > 100_000, $"Blob sollte gross sein, ist aber nur {blob.Length} Bytes");
    }

    [Fact]
    public void ReadFile_Lyric_ParsesHeader()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);

        Assert.True(header.IsBigEndian);
        Assert.Equal(12, header.NumOfElements);
        Assert.True(blob.Length > 1000);
    }

    // ── ReadFile: Klassen-Definitionen ──────────────────────────

    [Fact]
    public void ReadFile_CaliforniaLove_ContainsExpectedClasses()
    {
        var (header, _) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var classNames = header.Classes.ClassList.Select(c => c.Name).ToHashSet();

        Assert.Contains("lpsChart", classNames);
        Assert.Contains("lpsMusicInfo", classNames);
        Assert.Contains("lpsMusicIndex", classNames);
        Assert.Contains("ls2MusicData", classNames);
        Assert.Contains("lpsMelodyMarker", classNames);
        Assert.Contains("lpsLyricMarker", classNames);
        Assert.Contains("lpsHitMarker", classNames);
        Assert.Contains("ixSeqTempoCode", classNames);
        Assert.Contains("ixSequence", classNames);
        Assert.Contains("Tone", classNames);
    }

    [Fact]
    public void ReadFile_Lyric_ContainsRawFileImageClass()
    {
        var (header, _) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var classNames = header.Classes.ClassList.Select(c => c.Name).ToHashSet();

        Assert.Contains("ixRawFileImage", classNames);
        Assert.Contains("ixAssetPackage", classNames);
        Assert.Contains("ixPackage", classNames);
    }

    // ── Vererbung ───────────────────────────────────────────────

    [Fact]
    public void ReadFile_InheritanceIsResolved()
    {
        var (header, _) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var classLookup = header.Classes.ClassList.ToDictionary(c => c.Name);

        // lpsChart erbt von ixChart -> ixAgentPrototype -> ixPrototype -> ixAsset -> ...
        var lpsChart = classLookup["lpsChart"];
        Assert.NotNull(lpsChart.Parent);
        Assert.Equal("ixChart", lpsChart.Parent.Name);

        // ixChart erbt von ixAgentPrototype
        Assert.NotNull(lpsChart.Parent.Parent);
        Assert.Equal("ixAgentPrototype", lpsChart.Parent.Parent.Name);
    }

    [Fact]
    public void AllMembers_IncludesInheritedMembers()
    {
        var (header, _) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var classLookup = header.Classes.ClassList.ToDictionary(c => c.Name);

        var lpsChart = classLookup["lpsChart"];
        var allMembers = lpsChart.AllMembers;
        var memberNames = allMembers.Select(m => m.Name).ToHashSet();

        // Eigene Members von lpsChart
        Assert.Contains("m_strNoiseMaker", memberNames);
        Assert.Contains("m_BaseCentOffset", memberNames);
        Assert.Contains("m_pIndex", memberNames);
        Assert.Contains("m_pMusicData", memberNames);

        // Geerbte Members von ixChart
        Assert.Contains("m_vpSequence", memberNames);
        Assert.Contains("m_MusicStartOffset", memberNames);

        // Geerbte Members von ixAsset
        Assert.Contains("m_strName", memberNames);

        // Geerbte Members von ixReferencedObject
        Assert.Contains("m_uiReferenceCount", memberNames);
    }

    // ── Klassen-Größen ──────────────────────────────────────────

    [Theory]
    [InlineData("lpsChart", 184)]
    [InlineData("ixSequence", 104)]
    [InlineData("ixSeqTempoCode", 44)]
    [InlineData("lpsMelodyMarker", 40)]
    [InlineData("lpsLyricMarker", 64)]
    [InlineData("lpsHitMarker", 48)]
    [InlineData("Tone", 8)]
    [InlineData("lpsPhraseMarker", 40)]
    [InlineData("lpsPageBreakMarker", 24)]
    [InlineData("ixPackage", 72)]
    [InlineData("ixAssetPackage", 92)]
    public void ClassSizes_MatchExpected(string className, int expectedSize)
    {
        var (header, _) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var cls = header.Classes.ClassList.First(c => c.Name == className);
        Assert.Equal(expectedSize, cls.Size);
    }
}
