namespace LipsSongExtractor.Tests;

public class StfsWriterTests
{
    [Fact]
    public void CreatePackage_HasLiveMagic()
    {
        var files = new Dictionary<string, byte[]>
        {
            { "test.txt", "Hello World"u8.ToArray() }
        };

        var pkg = StfsWriter.CreatePackage(files, "Test", "Description");
        var magic = System.Text.Encoding.ASCII.GetString(pkg, 0, 4);

        Assert.Equal("LIVE", magic);
    }

    [Fact]
    public void CreatePackage_HasCorrectTitleId()
    {
        var files = new Dictionary<string, byte[]>
        {
            { "test.txt", "Hello"u8.ToArray() }
        };

        var pkg = StfsWriter.CreatePackage(files, "Test", "Desc");
        var titleId = (uint)((pkg[0x360] << 24) | (pkg[0x361] << 16) |
                             (pkg[0x362] << 8) | pkg[0x363]);

        Assert.Equal(0x4D530888u, titleId);
    }

    [Fact]
    public void CreatePackage_HasDisplayName()
    {
        var files = new Dictionary<string, byte[]>
        {
            { "test.txt", "Hello"u8.ToArray() }
        };

        var pkg = StfsWriter.CreatePackage(files, "My Song", "Desc");
        var name = System.Text.Encoding.BigEndianUnicode.GetString(pkg, 0x411, 0x80).TrimEnd('\0');

        Assert.Equal("My Song", name);
    }

    [Fact]
    public void CreatePackage_CanBeReadByStfsReader()
    {
        var files = new Dictionary<string, byte[]>
        {
            { "DLC.xml", "<DLC>test</DLC>"u8.ToArray() },
            { "Song.X360", new byte[500] },
            { "Song.jpg", new byte[100] }
        };

        var pkg = StfsWriter.CreatePackage(files, "Test Song", "A test song");

        var tempPath = Path.Combine(Path.GetTempPath(), $"stfs_write_test_{Guid.NewGuid()}");
        try
        {
            File.WriteAllBytes(tempPath, pkg);

            using var reader = new StfsReader(tempPath);
            Assert.Equal("LIVE", reader.Magic);
            Assert.Equal("Test Song", reader.DisplayName);
            Assert.Equal(3, reader.Files.Count);

            var dlcData = reader.ExtractFile("DLC.xml");
            Assert.NotNull(dlcData);
            var dlcText = System.Text.Encoding.UTF8.GetString(dlcData);
            Assert.Equal("<DLC>test</DLC>", dlcText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreatePackage_PreservesAllFileContents()
    {
        var originalData = new byte[5000];
        new Random(42).NextBytes(originalData);

        var files = new Dictionary<string, byte[]>
        {
            { "data.bin", originalData }
        };

        var pkg = StfsWriter.CreatePackage(files, "Test", "Desc");

        var tempPath = Path.Combine(Path.GetTempPath(), $"stfs_content_test_{Guid.NewGuid()}");
        try
        {
            File.WriteAllBytes(tempPath, pkg);

            using var reader = new StfsReader(tempPath);
            var extracted = reader.ExtractFile("data.bin");

            Assert.NotNull(extracted);
            Assert.Equal(originalData.Length, extracted.Length);
            Assert.True(originalData.AsSpan().SequenceEqual(extracted),
                "Extrahierte Daten muessen identisch mit Original sein");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreatePackage_MultipleFiles_AllExtractable()
    {
        var files = new Dictionary<string, byte[]>
        {
            { "file1.txt", "Content One"u8.ToArray() },
            { "file2.txt", "Content Two"u8.ToArray() },
            { "file3.bin", new byte[8192] } // >1 Block
        };

        var pkg = StfsWriter.CreatePackage(files, "Multi", "Multiple files");

        var tempPath = Path.Combine(Path.GetTempPath(), $"stfs_multi_test_{Guid.NewGuid()}");
        try
        {
            File.WriteAllBytes(tempPath, pkg);

            using var reader = new StfsReader(tempPath);
            Assert.Equal(3, reader.Files.Count);

            var f1 = reader.ExtractFile("file1.txt");
            var f2 = reader.ExtractFile("file2.txt");
            var f3 = reader.ExtractFile("file3.bin");

            Assert.Equal("Content One", System.Text.Encoding.UTF8.GetString(f1!));
            Assert.Equal("Content Two", System.Text.Encoding.UTF8.GetString(f2!));
            Assert.Equal(8192, f3!.Length);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void CreatePackage_ContentIdIsHeaderHash()
    {
        var files = new Dictionary<string, byte[]>
        {
            { "DLC.xml", "<DLC>test</DLC>"u8.ToArray() },
            { "Song.X360", new byte[500] }
        };

        var pkg = StfsWriter.CreatePackage(files, "Test", "Desc");

        // ContentID (0x32C, 20 Bytes) muss SHA1(header[0x344..firstBlock]) sein
        var headerSize = (uint)((pkg[0x340] << 24) | (pkg[0x341] << 16) |
                                (pkg[0x342] << 8) | pkg[0x343]);
        var firstBlock = (int)((headerSize + 0xFFF) & 0xFFFFF000);
        var expected = System.Security.Cryptography.SHA1.HashData(
            pkg.AsSpan(0x344, firstBlock - 0x344));

        Assert.True(expected.AsSpan().SequenceEqual(pkg.AsSpan(0x32C, 20)),
            "ContentID muss der SHA1-Hash des Metadata-Headers sein");
    }

    [Fact]
    public void OriginalDlc_ContentIdIsHeaderHash()
    {
        // Verifiziert die Hash-Formel gegen ein Original-DLC:
        // ContentID == SHA1(header[0x344..firstBlock]) == Dateiname[0..39]
        var path = Path.Combine(TestHelpers.ExampleDir,
            "DLC", "2A954A901B11AF81C0B6F77F699FA051A30985104D");
        if (!File.Exists(path)) return;

        var header = new byte[0xB000];
        using (var fs = File.OpenRead(path))
            fs.ReadExactly(header, 0, header.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(
            header.AsSpan(0x344, 0xB000 - 0x344));

        Assert.True(hash.AsSpan().SequenceEqual(header.AsSpan(0x32C, 20)),
            "Original-DLC: ContentID muss SHA1 des Metadata-Headers sein");

        var expectedName = Convert.ToHexString(hash) + "4D";
        Assert.Equal(expectedName, Path.GetFileName(path));
    }

    [Fact]
    public void CreateFromTemplate_ContentIdIsHeaderHash()
    {
        var templatePath = Path.Combine(TestHelpers.ExampleDir,
            "DLC", "2A954A901B11AF81C0B6F77F699FA051A30985104D");
        if (!File.Exists(templatePath)) return;

        var templateBytes = File.ReadAllBytes(templatePath);
        var files = new Dictionary<string, byte[]>
        {
            { "DLC.xml", "<DLC>test</DLC>"u8.ToArray() },
            { "Song.X360", new byte[500] }
        };

        var pkg = StfsWriter.CreateFromTemplate(templateBytes, files, "Changed Name");

        var headerSize = (uint)((pkg[0x340] << 24) | (pkg[0x341] << 16) |
                                (pkg[0x342] << 8) | pkg[0x343]);
        var firstBlock = (int)((headerSize + 0xFFF) & 0xFFFFF000);
        var expected = System.Security.Cryptography.SHA1.HashData(
            pkg.AsSpan(0x344, firstBlock - 0x344));

        Assert.True(expected.AsSpan().SequenceEqual(pkg.AsSpan(0x32C, 20)),
            "ContentID muss nach Template-Modifikation neu berechnet werden");

        // ContentID darf NICHT mehr die des Templates sein (Header wurde veraendert)
        Assert.False(templateBytes.AsSpan(0x32C, 20).SequenceEqual(pkg.AsSpan(0x32C, 20)),
            "ContentID muss sich vom Template unterscheiden wenn der Header geaendert wurde");
    }

    [Fact]
    public void CreateFromTemplate_RoundtripCaptainJack()
    {
        var templatePath = Path.Combine(TestHelpers.ExampleDir,
            "DLC", "4D530888", "00000002",
            "BE96573D8A5ABA64C98861ACD38688C34E7E80CF4D");
        if (!File.Exists(templatePath)) return;

        // Extrahiere alle Dateien aus dem Original
        var templateBytes = File.ReadAllBytes(templatePath);
        using var origReader = new StfsReader(templatePath);
        var origFiles = new Dictionary<string, byte[]>();
        foreach (var f in origReader.Files.Where(f => !f.IsDirectory))
            origFiles[f.Name] = origReader.ExtractFile(f);

        // Repacke mit Template
        var repackedBytes = StfsWriter.CreateFromTemplate(templateBytes, origFiles);

        var tempPath = Path.Combine(Path.GetTempPath(), $"stfs_repack_test_{Guid.NewGuid()}");
        try
        {
            File.WriteAllBytes(tempPath, repackedBytes);

            // Lese das repackte Paket
            using var repackReader = new StfsReader(tempPath);
            Assert.Equal(origFiles.Count, repackReader.Files.Count);

            // Jede Datei muss byte-identisch extrahierbar sein
            foreach (var f in repackReader.Files.Where(f => !f.IsDirectory))
            {
                var extracted = repackReader.ExtractFile(f);
                Assert.True(origFiles.ContainsKey(f.Name), $"Datei {f.Name} nicht im Original");
                Assert.Equal(origFiles[f.Name].Length, extracted.Length);
                Assert.True(origFiles[f.Name].AsSpan().SequenceEqual(extracted),
                    $"Datei {f.Name} ist nach Repack nicht byte-identisch");
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
