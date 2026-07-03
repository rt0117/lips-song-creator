namespace LipsSongExtractor.Tests;

public class IxbSerializerTests
{
    // ── Grundfunktionen ─────────────────────────────────────────

    [Fact]
    public void GetBlob_ReturnsClone()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var ser = new IxbSerializer(blob, header);

        var result = ser.GetBlob();
        Assert.Equal(blob.Length, result.Length);

        // Muss eine Kopie sein, kein Verweis
        result[0] = 0xFF;
        var result2 = ser.GetBlob();
        Assert.NotEqual(0xFF, result2[0]);
    }

    [Fact]
    public void GetBlob_InitiallyMatchesOriginal()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var ser = new IxbSerializer(blob, header);

        var result = ser.GetBlob();
        Assert.True(blob.AsSpan().SequenceEqual(result),
            "Initialer Blob muss identisch mit Original sein");
    }

    // ── SetField ────────────────────────────────────────────────

    [Fact]
    public void SetField_ModifiesBlob()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var ser = new IxbSerializer(blob, header);

        // Ändere m_BaseCentOffset von 0 auf 42
        var result = ser.SetField("lpsChart", "m_BaseCentOffset", 42);
        Assert.True(result, "SetField sollte erfolgreich sein");

        // Prüfe dass der Blob geändert wurde
        var modifiedBlob = ser.GetBlob();
        Assert.False(blob.AsSpan().SequenceEqual(modifiedBlob),
            "Blob sollte sich geändert haben");

        // Verifiziere den Wert über den Deserializer
        var deser = new IxbDeserializer(modifiedBlob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;
        Assert.Equal(42u, chart["m_BaseCentOffset"]);
    }

    [Fact]
    public void SetField_Float_Works()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var ser = new IxbSerializer(blob, header);

        var result = ser.SetField("lpsChart", "m_MusicStartOffset", 1.5f);
        Assert.True(result);

        var deser = new IxbDeserializer(ser.GetBlob(), header);
        var chart = deser.FindAndReadObject("lpsChart")!;

        // m_MusicStartOffset könnte als uint oder float gelesen werden
        var val = chart["m_MusicStartOffset"];
        // Der Wert sollte sich geändert haben (nicht mehr 0)
        Assert.NotEqual(0u, val);
    }

    [Fact]
    public void SetField_NonExistentClass_ReturnsFalse()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var ser = new IxbSerializer(blob, header);

        Assert.False(ser.SetField("NonExistentClass", "m_field", 42));
    }

    [Fact]
    public void SetField_NonExistentField_ReturnsFalse()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var ser = new IxbSerializer(blob, header);

        Assert.False(ser.SetField("lpsChart", "m_nonExistentField", 42));
    }

    // ── SetString ───────────────────────────────────────────────

    [Fact]
    public void SetString_ShorterString_Works()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var ser = new IxbSerializer(blob, header);

        // Ändere den Namen von "California Love_Lyric" auf "Test"
        var result = ser.SetString("ixRawFileImage", "m_strName", "Test");
        Assert.True(result, "SetString sollte erfolgreich sein");

        // Verifiziere über den Deserializer
        var deser = new IxbDeserializer(ser.GetBlob(), header);
        var obj = deser.FindAndReadObject("ixRawFileImage")!;
        Assert.Equal("Test", obj["m_strName"]);
    }

    [Fact]
    public void SetString_SameLength_Works()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var ser = new IxbSerializer(blob, header);

        // "Text" -> "Data" (gleiche Länge)
        var result = ser.SetString("ixRawFileImage", "m_strTypeName", "Data");
        Assert.True(result);

        var deser = new IxbDeserializer(ser.GetBlob(), header);
        var obj = deser.FindAndReadObject("ixRawFileImage")!;
        Assert.Equal("Data", obj["m_strTypeName"]);
    }

    [Fact]
    public void SetString_TooLong_ReturnsFalse()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var ser = new IxbSerializer(blob, header);

        // "Text" (5 Bytes inkl. null) -> String der definitiv zu lang ist
        var longStr = new string('X', 1000);
        var result = ser.SetString("ixRawFileImage", "m_strTypeName", longStr);
        Assert.False(result, "Zu langer String sollte fehlschlagen");
    }

    [Fact]
    public void SetString_NonExistentField_ReturnsFalse()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);
        var ser = new IxbSerializer(blob, header);

        Assert.False(ser.SetString("ixRawFileImage", "m_nonExistent", "Test"));
    }

    // ── Roundtrip mit Modifikation ──────────────────────────────

    [Fact]
    public void ModifiedBlob_CanBeWrittenAndReadBack()
    {
        var (headerRaw, blobOrig, trailer) =
            X360Writer.ReadRawParts(TestHelpers.CaliforniaLoveLyricX360);
        var (header, _) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveLyricX360);

        var ser = new IxbSerializer(blobOrig, header);
        ser.SetString("ixRawFileImage", "m_strTypeName", "Mod");

        var tempPath = Path.Combine(Path.GetTempPath(), "modified_roundtrip.x360");
        try
        {
            X360Writer.WriteFileRaw(tempPath, headerRaw, ser.GetBlob(), trailer);

            // Muss vom Reader gelesen werden können
            var (readHeader, readBlob) = X360Reader.ReadFile(tempPath);
            Assert.True(readHeader.IsBigEndian);
            Assert.Equal(blobOrig.Length, readBlob.Length);

            // Der modifizierte Wert muss lesbar sein
            var deser = new IxbDeserializer(readBlob, readHeader);
            var obj = deser.FindAndReadObject("ixRawFileImage")!;
            Assert.Equal("Mod", obj["m_strTypeName"]);

            // Andere Felder unverändert
            Assert.Equal("California Love_Lyric", obj["m_strName"]);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ModifiedChart_PreservesOtherFields()
    {
        var (header, blob) = X360Reader.ReadFile(TestHelpers.CaliforniaLoveX360);
        var ser = new IxbSerializer(blob, header);

        // Ändere nur m_BaseCentOffset
        ser.SetField("lpsChart", "m_BaseCentOffset", 100);
        var modBlob = ser.GetBlob();

        // Verifiziere: m_strName und m_strNoiseMaker bleiben gleich
        var deser = new IxbDeserializer(modBlob, header);
        var chart = deser.FindAndReadObject("lpsChart")!;
        Assert.Equal("California Love", chart["m_strName"]);
        Assert.Equal("Set003", chart["m_strNoiseMaker"]);
        Assert.Equal(100u, chart["m_BaseCentOffset"]);
    }
}
