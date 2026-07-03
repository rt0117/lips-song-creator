namespace LipsSongExtractor.Tests;

public class BlobAnalyzerTests
{
    // ── RU32 / RI32 / RF32 ──────────────────────────────────────

    [Fact]
    public void RU32_ReadsBigEndian()
    {
        var data = new byte[] { 0x3A, 0x38, 0x06, 0xA0 };
        Assert.Equal(0x3A3806A0u, BlobAnalyzer.RU32(data, 0));
    }

    [Fact]
    public void RU32_ReadsFromOffset()
    {
        var data = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x17 };
        Assert.Equal(0x00000017u, BlobAnalyzer.RU32(data, 2));
    }

    [Fact]
    public void RI32_ReadsNegative()
    {
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFE };
        Assert.Equal(-2, BlobAnalyzer.RI32(data, 0));
    }

    [Fact]
    public void RF32_ReadsFloat()
    {
        var data = new byte[] { 0x42, 0xF0, 0x00, 0x00 };
        Assert.Equal(120.0f, BlobAnalyzer.RF32(data, 0));
    }

    // ── ReadStr ─────────────────────────────────────────────────

    [Fact]
    public void ReadStr_ReadsNullTerminated()
    {
        var data = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x57 };
        Assert.Equal("Hello", BlobAnalyzer.ReadStr(data, 0));
    }

    [Fact]
    public void ReadStr_InvalidOffset_ReturnsInvalid()
    {
        var data = new byte[] { 0x41, 0x00 };
        Assert.Equal("<invalid>", BlobAnalyzer.ReadStr(data, 99));
    }

    // ── Integration mit echten Dateien ──────────────────────────

    [Fact]
    public void Lyric_Blob_ContainsCaliforniaLoveString()
    {
        var (_, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);

        // Suche "California Love_Lyric" im Blob
        var needle = System.Text.Encoding.ASCII.GetBytes("California Love_Lyric");
        var found = false;
        for (var i = 0; i <= blob.Length - needle.Length; i++)
        {
            if (blob.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "String 'California Love_Lyric' nicht im Blob gefunden");
    }

    [Fact]
    public void Main_Blob_ContainsCaliforniaLoveString()
    {
        var (_, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);

        var needle = System.Text.Encoding.ASCII.GetBytes("California Love");
        var found = false;
        for (var i = 0; i <= blob.Length - needle.Length; i++)
        {
            if (blob.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                found = true;
                break;
            }
        }

        Assert.True(found, "String 'California Love' nicht im Blob gefunden");
    }
}
