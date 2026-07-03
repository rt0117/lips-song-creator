namespace LipsSongExtractor.Tests;

public class X360WriterTests
{
    // ── ReadRawParts ────────────────────────────────────────────

    [Fact]
    public void ReadRawParts_CaliforniaLove_HeaderIsNotEmpty()
    {
        var (header, blob, trailer) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveX360);

        Assert.True(header.Length > 1000, $"Header sollte gross sein, ist nur {header.Length}");
        Assert.True(blob.Length > 100_000, $"Blob sollte gross sein, ist nur {blob.Length}");
        Assert.True(trailer.Length > 0, "Trailer sollte nicht leer sein");
    }

    [Fact]
    public void ReadRawParts_HeaderStartsWithXml()
    {
        var (header, _, _) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveX360);
        var headerStr = System.Text.Encoding.UTF8.GetString(header);
        Assert.StartsWith("<ixb", headerStr.TrimStart());
    }

    [Fact]
    public void ReadRawParts_TrailerContainsCloseTags()
    {
        var (_, _, trailer) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveX360);
        var trailerStr = System.Text.Encoding.ASCII.GetString(trailer);
        Assert.Contains("</Objects>", trailerStr);
        Assert.Contains("</ixb>", trailerStr);
    }

    [Fact]
    public void ReadRawParts_BlobMatchesReaderBlob()
    {
        // Der Blob aus ReadRawParts muss identisch sein mit dem aus X360Reader.ReadFile
        var (_, blobRaw, _) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveX360);
        var (_, blobReader) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);

        Assert.Equal(blobReader.Length, blobRaw.Length);
        Assert.True(blobReader.AsSpan().SequenceEqual(blobRaw),
            "Blob aus ReadRawParts muss identisch mit X360Reader.ReadFile sein");
    }

    // ── Roundtrip: Lesen -> Schreiben -> Vergleichen ────────────

    [Fact]
    public void Roundtrip_CaliforniaLove_ByteIdentical()
    {
        var originalBytes = File.ReadAllBytes(TestHelpers.CaliforniaLoveX360);
        var (header, blob, trailer) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveX360);

        var tempPath = Path.Combine(Path.GetTempPath(), "roundtrip_california_love.x360");
        try
        {
            X360Writer.WriteFileRaw(tempPath, header, blob, trailer);
            var writtenBytes = File.ReadAllBytes(tempPath);

            Assert.Equal(originalBytes.Length, writtenBytes.Length);
            Assert.True(originalBytes.AsSpan().SequenceEqual(writtenBytes),
                "Roundtrip muss Byte-fuer-Byte identisch sein");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void Roundtrip_Lyric_ByteIdentical()
    {
        var originalBytes = File.ReadAllBytes(TestHelpers.CaliforniaLoveLyricX360);
        var (header, blob, trailer) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveLyricX360);

        var tempPath = Path.Combine(Path.GetTempPath(), "roundtrip_lyric.x360");
        try
        {
            X360Writer.WriteFileRaw(tempPath, header, blob, trailer);
            var writtenBytes = File.ReadAllBytes(tempPath);

            Assert.Equal(originalBytes.Length, writtenBytes.Length);
            Assert.True(originalBytes.AsSpan().SequenceEqual(writtenBytes),
                "Lyric-Roundtrip muss Byte-fuer-Byte identisch sein");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // ── WriteFile (vereinfacht) ─────────────────────────────────

    [Fact]
    public void WriteFile_ProducesValidX360()
    {
        var (header, blob, _) = X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveLyricX360);

        var tempPath = Path.Combine(Path.GetTempPath(), "write_test.x360");
        try
        {
            X360Writer.WriteFile(tempPath, header, blob);
            var writtenBytes = File.ReadAllBytes(tempPath);
            var content = System.Text.Encoding.ASCII.GetString(writtenBytes);

            // Muss <Objects> und </Objects></ixb> enthalten
            Assert.Contains("<Objects>", content);
            Assert.Contains("</Objects></ixb>", content);

            // Muss vom Reader gelesen werden können
            var (parsedHeader, parsedBlob) = X360Reader.ReadFile(tempPath);
            Assert.True(parsedHeader.IsBigEndian);
            Assert.Equal(blob.Length, parsedBlob.Length);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
