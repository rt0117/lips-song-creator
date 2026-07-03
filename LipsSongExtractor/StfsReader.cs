using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Liest Xbox 360 STFS-Container (LIVE/PIRS/CON Pakete).
/// 
/// STFS = Secure Transacted File System, das Container-Format fuer
/// Xbox 360 DLC, Savegames und Titel-Updates.
/// 
/// Struktur:
///   0x0000: Magic ("LIVE", "PIRS" oder "CON ")
///   0x0004: Signatur (RSA)
///   0x022C: Licensing Data
///   0x032C: Content ID / Header SHA1
///   0x0340: Header Size (BE)
///   0x0344: Content Type (BE)
///   0x0360: Title ID (BE)
///   0x0379: Volume Descriptor
///   0x0411: Display Name (UTF-16 BE)
///   0x1611: Title Name (UTF-16 BE)
///   danach: Hash-Tables + Daten-Bloecke (je 0x1000)
/// 
/// Block-Layout (Read-Only/LIVE-Pakete):
///   Vor jeder Gruppe von 170 Daten-Bloecken liegt 1 Hash-Table-Block.
///   Bei grossen Paketen (>170*170 Bloecke) kommen Level-1-Tables dazu.
/// </summary>
public class StfsReader : IDisposable
{
    private const int BlockSize = 0x1000;
    private const int BlocksPerHashTable = 0xAA; // 170

    private readonly FileStream _stream;
    private readonly int _firstBlockOffset;
    private readonly int _packageShift; // 0 = read-only (1 Hash-Table), 1 = writeable (2 Hash-Tables)

    public string Magic { get; }
    public uint ContentType { get; }
    public uint TitleId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public List<StfsFileEntry> Files { get; } = [];

    public StfsReader(string path)
    {
        _stream = File.OpenRead(path);

        var header = new byte[0x1000];
        _stream.ReadExactly(header, 0, 0x1000);

        Magic = Encoding.ASCII.GetString(header, 0, 4);
        if (Magic != "LIVE" && Magic != "PIRS" && Magic != "CON ")
            throw new InvalidDataException($"Kein STFS-Container (Magic='{Magic}')");

        var headerSize = ReadU32BE(header, 0x340);
        ContentType = ReadU32BE(header, 0x344);
        TitleId = ReadU32BE(header, 0x360);
        DisplayName = Encoding.BigEndianUnicode.GetString(header, 0x411, 0x80).TrimEnd('\0');
        Description = Encoding.BigEndianUnicode.GetString(header, 0xD11, 0x80).TrimEnd('\0');

        // Volume Descriptor at 0x379
        var blockSeparation = header[0x37B];
        var fileTableBlockCount = (ushort)(header[0x37C] | (header[0x37D] << 8)); // LE
        var fileTableBlockNum = header[0x37E] | (header[0x37F] << 8) | (header[0x380] << 16); // int24 LE

        // Read-only Pakete (LIVE/PIRS): 1 Hash-Table pro Gruppe -> shift 0
        // Writeable (CON): 2 Hash-Tables -> shift 1
        _packageShift = (blockSeparation & 1) == 1 ? 0 : 1;

        _firstBlockOffset = (int)((headerSize + 0xFFF) & 0xFFFFF000);

        ReadFileTable(fileTableBlockNum, fileTableBlockCount);
    }

    /// <summary>
    /// Konvertiert eine Daten-Block-Nummer in den Datei-Offset.
    /// Beruecksichtigt die eingestreuten Hash-Table-Bloecke.
    /// </summary>
    private long BlockToOffset(int blockNum)
    {
        var backingBlock = ComputeBackingBlock(blockNum);
        return _firstBlockOffset + (long)backingBlock * BlockSize;
    }

    private int ComputeBackingBlock(int blockNum)
    {
        var result = ((blockNum + BlocksPerHashTable) / BlocksPerHashTable) << _packageShift;
        result += blockNum;

        if (blockNum < BlocksPerHashTable)
            return result;

        result += ((blockNum + 0x70E4) / 0x70E4) << _packageShift;

        if (blockNum < 0x70E4)
            return result;

        return result + (1 << _packageShift);
    }

    /// <summary>
    /// Liest den naechsten Block einer Block-Kette aus der Hash-Table.
    /// </summary>
    private int GetNextBlock(int blockNum)
    {
        // Hash-Entry fuer blockNum liegt in der Hash-Table VOR der Block-Gruppe
        var tableIndex = blockNum / BlocksPerHashTable;
        var tableBlock = tableIndex * (BlocksPerHashTable + (1 << _packageShift));

        // Hash-Table Offset: direkt vor der Gruppe
        long tableOffset;
        if (blockNum < BlocksPerHashTable)
        {
            tableOffset = _firstBlockOffset;
        }
        else
        {
            // Backing-Block der Tabelle berechnen
            var firstDataBlockOfGroup = tableIndex * BlocksPerHashTable;
            var backing = ComputeBackingBlock(firstDataBlockOfGroup);
            tableOffset = _firstBlockOffset + (long)(backing - 1) * BlockSize;
        }

        var entryOffset = tableOffset + (blockNum % BlocksPerHashTable) * 0x18;

        var entry = new byte[0x18];
        _stream.Seek(entryOffset, SeekOrigin.Begin);
        _stream.ReadExactly(entry, 0, 0x18);

        // Next Block: int24 BE at 0x15
        return (entry[0x15] << 16) | (entry[0x16] << 8) | entry[0x17];
    }

    private void ReadFileTable(int startBlock, int blockCount)
    {
        var tableData = new byte[blockCount * BlockSize];
        var block = startBlock;

        for (var i = 0; i < blockCount; i++)
        {
            var offset = BlockToOffset(block);
            _stream.Seek(offset, SeekOrigin.Begin);
            _stream.ReadExactly(tableData, i * BlockSize, BlockSize);

            if (i + 1 < blockCount)
                block = GetNextBlock(block);
        }

        // Parse Entries (0x40 Bytes pro Eintrag)
        for (var off = 0; off + 0x40 <= tableData.Length; off += 0x40)
        {
            var flags = tableData[off + 0x28];
            var nameLen = flags & 0x3F;
            if (nameLen == 0) continue;

            var entry = new StfsFileEntry
            {
                Name = Encoding.ASCII.GetString(tableData, off, Math.Min(nameLen, 0x28)),
                IsDirectory = (flags & 0x80) != 0,
                BlocksConsecutive = (flags & 0x40) != 0,
                BlockCount = tableData[off + 0x29] | (tableData[off + 0x2A] << 8) |
                             (tableData[off + 0x2B] << 16),
                StartBlock = tableData[off + 0x2F] | (tableData[off + 0x30] << 8) |
                             (tableData[off + 0x31] << 16),
                PathIndex = (short)((tableData[off + 0x32] << 8) | tableData[off + 0x33]),
                Size = ReadU32BE(tableData, off + 0x34),
            };

            Files.Add(entry);
        }
    }

    /// <summary>
    /// Extrahiert eine Datei aus dem Container.
    /// </summary>
    public byte[] ExtractFile(StfsFileEntry entry)
    {
        var result = new byte[entry.Size];
        var remaining = (long)entry.Size;
        var block = entry.StartBlock;
        var written = 0;

        while (remaining > 0 && block != 0xFFFFFF)
        {
            var offset = BlockToOffset(block);
            var toRead = (int)Math.Min(BlockSize, remaining);

            _stream.Seek(offset, SeekOrigin.Begin);
            _stream.ReadExactly(result, written, toRead);

            written += toRead;
            remaining -= toRead;

            if (remaining > 0)
            {
                block = entry.BlocksConsecutive ? block + 1 : GetNextBlock(block);
            }
        }

        return result;
    }

    /// <summary>
    /// Extrahiert eine Datei anhand des Namens.
    /// </summary>
    public byte[]? ExtractFile(string fileName)
    {
        var entry = Files.FirstOrDefault(f =>
            f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        return entry != null ? ExtractFile(entry) : null;
    }

    private static uint ReadU32BE(byte[] buf, int offset) =>
        (uint)((buf[offset] << 24) | (buf[offset + 1] << 16) |
               (buf[offset + 2] << 8) | buf[offset + 3]);

    public void Dispose() => _stream.Dispose();
}

public class StfsFileEntry
{
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool BlocksConsecutive { get; set; }
    public int BlockCount { get; set; }
    public int StartBlock { get; set; }
    public short PathIndex { get; set; }
    public uint Size { get; set; }
}
