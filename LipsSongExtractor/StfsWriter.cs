using System.Security.Cryptography;
using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Erzeugt Xbox 360 STFS LIVE-Pakete (unsigned, fuer RGH/JTAG/DevKit).
///
/// LIVE-Pakete haben:
/// - 0x971A Bytes Header (Signatur + Metadata + Volume Descriptor)
/// - Hash-Tables (Level 0: SHA1 pro Block, Level 1: SHA1 pro 170 L0-Tables)
/// - Daten-Bloecke (je 0x1000 Bytes)
///
/// Auf RGH/JTAG-Konsolen wird die RSA-Signatur nicht geprueft,
/// aber die SHA1 Hash-Tables muessen korrekt sein.
/// </summary>
public static class StfsWriter
{
    private const int BlockSize = 0x1000;
    private const int HashesPerTable = 0xAA; // 170
    private const int HeaderSize = 0xAD0E; // Mit Thumbnails (wie Original DLC)
    private const int FirstBlockOffset = 0xB000; // Header (0xAD0E) aligned to 0x1000 -> 0xB000

    /// <summary>
    /// Erzeugt ein LIVE-Paket basierend auf einem Original-DLC als Template.
    /// Kopiert den kompletten Header (inkl. Signatur, Thumbnails, Licensing),
    /// und ersetzt nur die Datei-Inhalte und die variablen Felder.
    /// </summary>
    public static byte[] CreateFromTemplate(
        byte[] templatePackage,
        Dictionary<string, byte[]> files,
        string? displayName = null,
        string? description = null)
    {
        // Header-Groesse aus dem Template lesen
        var templateHeaderSize = (uint)((templatePackage[0x340] << 24) | (templatePackage[0x341] << 16) |
                                       (templatePackage[0x342] << 8) | templatePackage[0x343]);
        var firstBlock = (int)((templateHeaderSize + 0xFFF) & 0xFFFFF000);

        // Dateien vorbereiten - DLC.xml MUSS zuerst kommen (Lips erwartet das)
        var sortedFiles = SortFilesForStfs(files);
        var fileEntries = new List<FileEntry>();
        var totalDataBlocks = 0;
        foreach (var (name, data) in sortedFiles)
        {
            var blocks = (data.Length + BlockSize - 1) / BlockSize;
            fileEntries.Add(new FileEntry
            {
                Name = name,
                Data = data,
                StartBlock = totalDataBlocks,
                BlockCount = blocks
            });
            totalDataBlocks += blocks;
        }

        var fileTableBlocks = 1;
        var totalBlocks = fileTableBlocks + totalDataBlocks;
        var hashTableCount = (totalBlocks + HashesPerTable - 1) / HashesPerTable;
        var totalBlocksWithHash = totalBlocks + hashTableCount;
        if (hashTableCount > 1) totalBlocksWithHash++; // Level-1

        // Blob zusammenbauen: Header vom Template, Daten neu
        var blobSize = firstBlock + totalBlocksWithHash * BlockSize;
        var blob = new byte[blobSize];

        // 1. Kompletten Header vom Template kopieren
        Array.Copy(templatePackage, 0, blob, 0, Math.Min(firstBlock, templatePackage.Length));

        // 2. Nur die variablen Felder aktualisieren
        var contentSize = (long)totalBlocks * BlockSize;
        WriteU32BE(blob, 0x34C, (uint)(contentSize >> 32));
        WriteU32BE(blob, 0x350, (uint)(contentSize & 0xFFFFFFFF));

        // File table meta
        blob[0x37C] = (byte)(fileTableBlocks & 0xFF);
        blob[0x37D] = (byte)((fileTableBlocks >> 8) & 0xFF);
        blob[0x37E] = 0; blob[0x37F] = 0; blob[0x380] = 0;

        // Total allocated blocks
        WriteU32BE(blob, 0x395, (uint)totalBlocks);

        // Display Name (optional ueberschreiben)
        if (displayName != null)
        {
            var nameBytes = Encoding.BigEndianUnicode.GetBytes(displayName);
            var nameTrunc = Math.Min(nameBytes.Length, 0x80);
            for (var lang = 0; lang < 9; lang++)
            {
                Array.Clear(blob, 0x411 + lang * 0x100, 0x80);
                Array.Copy(nameBytes, 0, blob, 0x411 + lang * 0x100, nameTrunc);
            }
        }

        if (description != null)
        {
            var descBytes = Encoding.BigEndianUnicode.GetBytes(description);
            var descTrunc = Math.Min(descBytes.Length, 0x80);
            for (var lang = 0; lang < 9; lang++)
            {
                Array.Clear(blob, 0xD11 + lang * 0x100, 0x80);
                Array.Copy(descBytes, 0, blob, 0xD11 + lang * 0x100, descTrunc);
            }
        }

        // 3. Daten-Bloecke sammeln
        var fileTableData = BuildFileTable(fileEntries);
        var allDataBlocks = new byte[totalBlocks * BlockSize];
        Array.Copy(fileTableData, 0, allDataBlocks, 0, Math.Min(fileTableData.Length, BlockSize));

        foreach (var fe in fileEntries)
        {
            var destOffset = (fe.StartBlock + fileTableBlocks) * BlockSize;
            Array.Copy(fe.Data, 0, allDataBlocks, destOffset,
                Math.Min(fe.Data.Length, fe.BlockCount * BlockSize));
        }

        // 4. Daten-Bloecke an korrekte physische Positionen schreiben
        //    ComputeDataBlockNumber liefert die physische Block-Position
        for (var i = 0; i < totalBlocks; i++)
        {
            var physBlock = ComputeDataBlockNumber(i);
            var physOffset = firstBlock + physBlock * BlockSize;
            if (physOffset + BlockSize > blob.Length)
                Array.Resize(ref blob, physOffset + BlockSize);
            Array.Copy(allDataBlocks, i * BlockSize, blob, physOffset, BlockSize);
        }

        // 5. Level-0 Hash-Tables schreiben (1 pro 170 Daten-Bloecke)
        for (var group = 0; group < hashTableCount; group++)
        {
            var hashTable = new byte[BlockSize];
            var groupStart = group * HashesPerTable;
            var groupEnd = Math.Min(groupStart + HashesPerTable, totalBlocks);

            for (var i = groupStart; i < groupEnd; i++)
            {
                var entryOffset = (i - groupStart) * 0x18;
                // SHA1 Hash des Daten-Blocks an seiner physischen Position
                var physBlock = ComputeDataBlockNumber(i);
                var physOffset = firstBlock + physBlock * BlockSize;
                var hash = SHA1.HashData(blob.AsSpan(physOffset, BlockSize));
                Array.Copy(hash, 0, hashTable, entryOffset, 20);
                hashTable[entryOffset + 0x14] = 0x80; // Used
                // Next block (consecutive data blocks)
                var nextBlock = i + 1;
                if (nextBlock >= totalBlocks) nextBlock = 0xFFFFFF;
                hashTable[entryOffset + 0x15] = (byte)((nextBlock >> 16) & 0xFF);
                hashTable[entryOffset + 0x16] = (byte)((nextBlock >> 8) & 0xFF);
                hashTable[entryOffset + 0x17] = (byte)(nextBlock & 0xFF);
            }

            // L0-Table Position: direkt vor der Gruppe von Daten-Bloecken
            var l0PhysBlock = ComputeLevel0TableBlock(group);
            var l0Offset = firstBlock + l0PhysBlock * BlockSize;
            if (l0Offset + BlockSize > blob.Length)
                Array.Resize(ref blob, l0Offset + BlockSize);
            Array.Copy(hashTable, 0, blob, l0Offset, BlockSize);
        }

        // 6. Level-1 Hash-Table (wenn > 1 L0-Table)
        if (hashTableCount > 1)
        {
            var level1Table = new byte[BlockSize];
            for (var g = 0; g < hashTableCount; g++)
            {
                var l0PhysBlock = ComputeLevel0TableBlock(g);
                var l0Offset = firstBlock + l0PhysBlock * BlockSize;
                var hash = SHA1.HashData(blob.AsSpan(l0Offset, BlockSize));
                var entryOffset = g * 0x18;
                Array.Copy(hash, 0, level1Table, entryOffset, 20);
                level1Table[entryOffset + 0x14] = 0x80;
            }

            var l1PhysBlock = ComputeLevel1TableBlock();
            var l1Offset = firstBlock + l1PhysBlock * BlockSize;
            if (l1Offset + BlockSize > blob.Length)
                Array.Resize(ref blob, l1Offset + BlockSize);
            Array.Copy(level1Table, 0, blob, l1Offset, BlockSize);
        }

        // Top hash = hash of the first hash table (L1 if exists, otherwise L0)
        int topTableBlock;
        if (hashTableCount > 1)
            topTableBlock = ComputeLevel1TableBlock();
        else
            topTableBlock = ComputeLevel0TableBlock(0);

        var topOffset = firstBlock + topTableBlock * BlockSize;
        var topHash = SHA1.HashData(blob.AsSpan(topOffset, BlockSize));
        Array.Copy(topHash, 0, blob, 0x381, 20);

        // ContentID (0x32C) = SHA1 des Metadata-Headers (0x344 bis Header-Ende).
        // Die XContent-API validiert diesen Hash auch auf RGH/JTAG-Konsolen -
        // ein falscher Wert fuehrt dazu, dass das Paket ignoriert wird.
        // MUSS als letztes berechnet werden (nach TopHash, DisplayName etc.).
        WriteContentIdHash(blob, firstBlock);

        // Trim to actual size
        var lastDataBlock = ComputeDataBlockNumber(totalBlocks - 1);
        var finalSize = firstBlock + (lastDataBlock + 1) * BlockSize;
        return blob.AsSpan(0, Math.Min(finalSize, blob.Length)).ToArray();
    }

    /// <summary>
    /// Erzeugt ein LIVE-Paket aus einem Dictionary von Dateiname->Daten.
    /// </summary>
    public static byte[] CreatePackage(
        Dictionary<string, byte[]> files,
        string displayName,
        string description,
        uint titleId = 0x4D530888, // Lips
        uint contentType = 0x00000002) // Marketplace Content
    {
        // Berechne Bloecke pro Datei - DLC.xml MUSS zuerst kommen
        var sortedFiles = SortFilesForStfs(files);
        var fileEntries = new List<FileEntry>();
        var totalDataBlocks = 0;

        foreach (var (name, data) in sortedFiles)
        {
            var blocks = (data.Length + BlockSize - 1) / BlockSize;
            fileEntries.Add(new FileEntry
            {
                Name = name,
                Data = data,
                StartBlock = totalDataBlocks,
                BlockCount = blocks
            });
            totalDataBlocks += blocks;
        }

        // File-Table (1 Block, max 64 Dateien a 0x40 Bytes)
        var fileTableBlocks = 1;
        var totalBlocks = fileTableBlocks + totalDataBlocks;

        // Berechne Hash-Table Bloecke (1 pro 170 Daten-Bloecke)
        var hashTableCount = (totalBlocks + HashesPerTable - 1) / HashesPerTable;
        var totalBlocksWithHash = totalBlocks + hashTableCount;

        // Level-1 Hash-Table noetig bei >170 Bloecke
        var hasLevel1 = hashTableCount > 1;
        if (hasLevel1)
            totalBlocksWithHash += 1; // 1 Level-1 Table

        // Blob zusammenbauen
        var blobSize = FirstBlockOffset + totalBlocksWithHash * BlockSize;
        var blob = new byte[blobSize];

        // ── Header schreiben ──────────────────────────────────
        WriteHeader(blob, displayName, description, titleId, contentType,
            totalBlocks, fileTableBlocks);

        // ── Daten-Bloecke schreiben (gleiche Logik wie CreateFromTemplate) ──
        var fileTableData = BuildFileTable(fileEntries);
        var allDataBlocks = new byte[totalBlocks * BlockSize];
        Array.Copy(fileTableData, 0, allDataBlocks, 0, Math.Min(fileTableData.Length, BlockSize));

        foreach (var fe in fileEntries)
        {
            var destOffset = (fe.StartBlock + fileTableBlocks) * BlockSize;
            Array.Copy(fe.Data, 0, allDataBlocks, destOffset,
                Math.Min(fe.Data.Length, fe.BlockCount * BlockSize));
        }

        // Daten-Bloecke an korrekte physische Positionen
        for (var i = 0; i < totalBlocks; i++)
        {
            var physBlock = ComputeDataBlockNumber(i);
            var physOffset = FirstBlockOffset + physBlock * BlockSize;
            if (physOffset + BlockSize > blob.Length)
                Array.Resize(ref blob, physOffset + BlockSize);
            Array.Copy(allDataBlocks, i * BlockSize, blob, physOffset, BlockSize);
        }

        // L0 Hash-Tables
        for (var group = 0; group < hashTableCount; group++)
        {
            var hashTable = new byte[BlockSize];
            var groupStart = group * HashesPerTable;
            var groupEnd = Math.Min(groupStart + HashesPerTable, totalBlocks);

            for (var i = groupStart; i < groupEnd; i++)
            {
                var entryOffset = (i - groupStart) * 0x18;
                var physBlock = ComputeDataBlockNumber(i);
                var physOffset = FirstBlockOffset + physBlock * BlockSize;
                var hash = SHA1.HashData(blob.AsSpan(physOffset, BlockSize));
                Array.Copy(hash, 0, hashTable, entryOffset, 20);
                hashTable[entryOffset + 0x14] = 0x80;
                var nextBlock = i + 1;
                if (nextBlock >= totalBlocks) nextBlock = 0xFFFFFF;
                hashTable[entryOffset + 0x15] = (byte)((nextBlock >> 16) & 0xFF);
                hashTable[entryOffset + 0x16] = (byte)((nextBlock >> 8) & 0xFF);
                hashTable[entryOffset + 0x17] = (byte)(nextBlock & 0xFF);
            }

            var l0PhysBlock = ComputeLevel0TableBlock(group);
            var l0Offset = FirstBlockOffset + l0PhysBlock * BlockSize;
            if (l0Offset + BlockSize > blob.Length)
                Array.Resize(ref blob, l0Offset + BlockSize);
            Array.Copy(hashTable, 0, blob, l0Offset, BlockSize);
        }

        // L1 Hash-Table
        if (hasLevel1)
        {
            var level1Table = new byte[BlockSize];
            for (var g = 0; g < hashTableCount; g++)
            {
                var l0PhysBlock = ComputeLevel0TableBlock(g);
                var l0Offset = FirstBlockOffset + l0PhysBlock * BlockSize;
                var hash = SHA1.HashData(blob.AsSpan(l0Offset, BlockSize));
                var entryOffset = g * 0x18;
                Array.Copy(hash, 0, level1Table, entryOffset, 20);
                level1Table[entryOffset + 0x14] = 0x80;
            }

            var l1PhysBlock = ComputeLevel1TableBlock();
            var l1Offset = FirstBlockOffset + l1PhysBlock * BlockSize;
            if (l1Offset + BlockSize > blob.Length)
                Array.Resize(ref blob, l1Offset + BlockSize);
            Array.Copy(level1Table, 0, blob, l1Offset, BlockSize);
        }

        // Top hash
        int topTableBlock = hasLevel1 ? ComputeLevel1TableBlock() : ComputeLevel0TableBlock(0);
        var topOffset = FirstBlockOffset + topTableBlock * BlockSize;
        var topHash = SHA1.HashData(blob.AsSpan(topOffset, BlockSize));
        Array.Copy(topHash, 0, blob, 0x381, 20);

        // ContentID = SHA1 des Metadata-Headers (siehe WriteContentIdHash)
        WriteContentIdHash(blob, FirstBlockOffset);

        var lastDataBlock = ComputeDataBlockNumber(totalBlocks - 1);
        var finalSize = FirstBlockOffset + (lastDataBlock + 1) * BlockSize;
        return blob.AsSpan(0, Math.Min(finalSize, blob.Length)).ToArray();
    }

    private static void WriteHeader(byte[] blob, string displayName, string description,
        uint titleId, uint contentType, int totalAllocBlocks, int fileTableBlockCount)
    {
        // Magic: "LIVE"
        Encoding.ASCII.GetBytes("LIVE").CopyTo(blob, 0);

        // Certificate Type: 0xC684 (wie im Original-DLC)
        blob[0x04] = 0xC6;
        blob[0x05] = 0x84;

        // Licensing Data at 0x22C
        // Eintrag 0: FFFFFFFFFFFFFFFF 00000001 = "unlocked for all"
        for (var i = 0; i < 8; i++) blob[0x22C + i] = 0xFF;
        WriteU32BE(blob, 0x234, 1);
        WriteU32BE(blob, 0x238, 0);

        // ContentID at 0x32C (20 Bytes) - wird NACH dem kompletten Aufbau
        // als SHA1 des Metadata-Headers berechnet (siehe WriteContentIdHash).
        // Der Dateiname ist dann ContentID(40 hex) + "4D".

        // Header Size (0xAD0E = mit Thumbnails, wie Original)
        WriteU32BE(blob, 0x340, (uint)HeaderSize);
        WriteU32BE(blob, 0x344, contentType);
        WriteU32BE(blob, 0x348, 2); // Metadata version

        // Content Size
        var contentSize = (long)totalAllocBlocks * BlockSize;
        WriteU32BE(blob, 0x34C, (uint)(contentSize >> 32));
        WriteU32BE(blob, 0x350, (uint)(contentSize & 0xFFFFFFFF));

        // Title ID
        WriteU32BE(blob, 0x360, titleId);

        // Volume Descriptor at 0x379
        blob[0x379] = 36;
        blob[0x37A] = 0;
        blob[0x37B] = 1; // Block separation: read-only layout

        // File table
        blob[0x37C] = (byte)(fileTableBlockCount & 0xFF);
        blob[0x37D] = (byte)((fileTableBlockCount >> 8) & 0xFF);
        blob[0x37E] = 0; blob[0x37F] = 0; blob[0x380] = 0;

        // Total allocated blocks
        WriteU32BE(blob, 0x395, (uint)totalAllocBlocks);
        WriteU32BE(blob, 0x399, 0);

        // Display Name (UTF-16 BE) - 9 Sprachslots (je 0x100 Bytes, 0x411-0xD11)
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(displayName);
        var nameTrunc = Math.Min(nameBytes.Length, 0x80);
        for (var lang = 0; lang < 9; lang++)
            Array.Copy(nameBytes, 0, blob, 0x411 + lang * 0x100, nameTrunc);

        // Description (UTF-16 BE) - 9 Sprachslots (0xD11-0x1611)
        var descBytes = Encoding.BigEndianUnicode.GetBytes(description);
        var descTrunc = Math.Min(descBytes.Length, 0x80);
        for (var lang = 0; lang < 9; lang++)
            Array.Copy(descBytes, 0, blob, 0xD11 + lang * 0x100, descTrunc);

        // Publisher Name at 0x1611 (0x80 Bytes) - leer bei Original-DLCs

        // Title Name at 0x1691 (0x80 Bytes) - KRITISCH: "Lips"!
        // Ohne diesen Namen ordnet die Xbox den DLC nicht dem Spiel zu
        var titleNameBytes = Encoding.BigEndianUnicode.GetBytes("Lips");
        Array.Copy(titleNameBytes, 0, blob, 0x1691, titleNameBytes.Length);

        // Transfer Flags at 0x1711: 0xC0 (wie Original-DLCs)
        blob[0x1711] = 0xC0;

        // Thumbnails: Slot 1 bei 0x171A (4000h Bytes), Slot 2 bei 0x571A (4000h Bytes)
        // Wir verwenden eingebettete Placeholder-PNGs
        var thumb = GetLipsThumbnail();
        var titleThumb = GetLipsTitleThumbnail();

        WriteU32BE(blob, 0x1712, (uint)thumb.Length);       // Thumbnail size
        WriteU32BE(blob, 0x1716, (uint)titleThumb.Length);  // Title thumbnail size

        if (thumb.Length > 0)
            Array.Copy(thumb, 0, blob, 0x171A, Math.Min(thumb.Length, 0x4000));
        if (titleThumb.Length > 0)
            Array.Copy(titleThumb, 0, blob, 0x571A, Math.Min(titleThumb.Length, 0x4000));
    }

    /// <summary>
    /// Minimales 1x1 PNG als Thumbnail-Platzhalter.
    /// Ein echter PNG ist noetig damit Lips das Paket akzeptiert.
    /// </summary>
    private static byte[] GetLipsThumbnail()
    {
        // Minimales 64x64 schwarzes PNG (Base64 encoded, generiert mit Python/PIL)
        // Lips erwartet ein gueltiges PNG-Bild, sonst wird der DLC ignoriert
        const string base64 =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAABHNCSVQICAgIfAhkiAAAAAlwSFlzAAAL" +
            "EwAACxMBAJqcGAAAABl0RVh0U29mdHdhcmUAd3d3Lmlua3NjYXBlLm9yZ5vuPBoAAABJSURBVHic7c" +
            "ExAQAAAMKoL+Z25Q8BAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
            "AAAAAAAAAADgNxWoAAFHLNpEAAAAAElFTkSuQmCC";
        return Convert.FromBase64String(base64);
    }

    private static byte[] GetLipsTitleThumbnail()
    {
        // Gleicher Platzhalter fuer das Title-Thumbnail
        return GetLipsThumbnail();
    }

    private static byte[] BuildFileTable(List<FileEntry> entries)
    {
        var table = new byte[BlockSize];

        for (var i = 0; i < entries.Count && i < 64; i++)
        {
            var e = entries[i];
            var off = i * 0x40;

            // Filename (max 0x28 Bytes ASCII)
            var nameBytes = Encoding.ASCII.GetBytes(e.Name);
            var nameLen = Math.Min(nameBytes.Length, 0x28);
            Array.Copy(nameBytes, 0, table, off, nameLen);

            // Flags: nameLen (kein consecutive bit - Xbox liest Block-Chain aus Hash-Table)
            table[off + 0x28] = (byte)nameLen;

            // Blocks for file (int24 LE)
            table[off + 0x29] = (byte)(e.BlockCount & 0xFF);
            table[off + 0x2A] = (byte)((e.BlockCount >> 8) & 0xFF);
            table[off + 0x2B] = (byte)((e.BlockCount >> 16) & 0xFF);

            // Copy of blocks? (3 bytes at 0x2C)
            table[off + 0x2C] = table[off + 0x29];
            table[off + 0x2D] = table[off + 0x2A];
            table[off + 0x2E] = table[off + 0x2B];

            // Start block (int24 LE) - +1 weil Block 0 = FileTable
            var startBlock = e.StartBlock + 1; // FileTable ist Block 0
            table[off + 0x2F] = (byte)(startBlock & 0xFF);
            table[off + 0x30] = (byte)((startBlock >> 8) & 0xFF);
            table[off + 0x31] = (byte)((startBlock >> 16) & 0xFF);

            // Path indicator (0xFFFF = root, LE)
            table[off + 0x32] = 0xFF;
            table[off + 0x33] = 0xFF;

            // File size (BE uint32)
            WriteU32BE(table, off + 0x34, (uint)e.Data.Length);

            // Update timestamp, access timestamp (0)
        }

        return table;
    }

    /// <summary>
    /// Berechnet die physische Block-Position eines Daten-Blocks.
    /// Identisch mit dem Reader's ComputeBackingBlock, konsistent mit Free60 Spec.
    /// LIVE-Pakete: shift=0 (single hash table copies).
    /// </summary>
    private static int ComputeDataBlockNumber(int dataBlock)
    {
        // Level 0: 1 Hash-Table-Block pro 170 Daten-Bloecke
        var result = ((dataBlock + HashesPerTable) / HashesPerTable) + dataBlock;

        if (dataBlock < HashesPerTable)
            return result;

        // Level 1: 1 Hash-Table-Block pro 170*170 Daten-Bloecke
        result += ((dataBlock + 0x70E4) / 0x70E4);

        // Level 2 wuerde erst ab 0x4AF768 Bloecken greifen (>19 GB) - nicht implementiert
        return result;
    }

    /// <summary>
    /// Berechnet die physische Position eines Level-0 Hash-Table-Blocks.
    /// Die L0-Table fuer Gruppe N liegt direkt VOR den Daten-Bloecken dieser Gruppe.
    /// </summary>
    private static int ComputeLevel0TableBlock(int groupIndex)
    {
        if (groupIndex == 0)
            return 0; // Erste L0-Table ist immer bei physischem Block 0

        // Die L0-Table fuer Gruppe N liegt bei der Position des ersten Daten-Blocks
        // der Gruppe MINUS 1. Aber wir muessen die L1-Table beruecksichtigen.
        var firstDataBlockOfGroup = groupIndex * HashesPerTable;
        return ComputeDataBlockNumber(firstDataBlockOfGroup) - 1;
    }

    /// <summary>
    /// Berechnet die physische Position des Level-1 Hash-Table-Blocks.
    /// Liegt nach der ersten Gruppe von 170 Daten-Bloecken + deren L0-Table.
    /// </summary>
    private static int ComputeLevel1TableBlock()
    {
        // Verifiziert gegen Original-DLCs:
        // Physical 0 = L0[0], Physical 1-170 = Data[0-169],
        // Physical 171 = L1, Physical 172 = L0[1], Physical 173 = Data[170], ...
        return HashesPerTable + 1; // 171
    }

    /// <summary>
    /// Sortiert Dateien fuer das STFS-Paket: DLC.xml zuerst, dann JPG, dann Charts, dann Audio.
    /// Lips erwartet DLC.xml als erstes Entry in der File-Table.
    /// </summary>
    private static List<KeyValuePair<string, byte[]>> SortFilesForStfs(Dictionary<string, byte[]> files)
    {
        return files.OrderBy(f =>
        {
            var name = f.Key.ToUpperInvariant();
            if (name == "DLC.XML") return 0;
            if (name.EndsWith(".JPG") || name.EndsWith(".JPEG")) return 1;
            if (name.EndsWith(".X360") && !name.Contains("_LYRIC")) return 2;
            if (name.EndsWith(".X360")) return 3; // Lyric
            if (name.EndsWith(".XWMA") && name.Contains("_PRV")) return 4;
            if (name.EndsWith(".XWMA")) return 5;
            if (name.EndsWith(".NFT")) return 6;
            if (name.EndsWith(".WMV")) return 7;
            if (name.EndsWith(".XML")) return 8; // GES*.xml
            return 9;
        }).ThenBy(f => f.Key).ToList();
    }

    /// <summary>
    /// Berechnet die ContentID (Header-Hash) und schreibt sie an Offset 0x32C.
    ///
    /// KRITISCH: Die ContentID ist KEIN zufaelliger Wert, sondern der
    /// SHA1-Hash des Metadata-Headers von 0x344 bis zum Block-alignten
    /// Header-Ende (z.B. 0xB000 bei HeaderSize 0xAD0E).
    /// Verifiziert gegen Original-DLCs:
    ///   SHA1(header[0x344..0xB000]) == ContentID == Dateiname[0..39]
    /// Die Xbox XContent-API validiert diesen Hash beim Content-Scan -
    /// auch auf RGH/JTAG. Ein Paket mit falschem Header-Hash wird ignoriert.
    /// Muss als LETZTES aufgerufen werden, nachdem alle Header-Felder
    /// (TopHash, ContentSize, DisplayName, ...) final sind.
    /// </summary>
    private static void WriteContentIdHash(byte[] blob, int firstBlock)
    {
        var hash = SHA1.HashData(blob.AsSpan(0x344, firstBlock - 0x344));
        Array.Copy(hash, 0, blob, 0x32C, 20);
    }

    /// <summary>
    /// Liest die ContentID aus einem STFS-Paket und gibt den korrekten Dateinamen zurueck.
    /// Format: ContentID(40 hex Zeichen) + "4D"
    /// </summary>
    public static string GetRequiredFileName(byte[] stfsPackage)
    {
        var contentId = new StringBuilder(40);
        for (var i = 0; i < 20; i++)
            contentId.AppendFormat("{0:X2}", stfsPackage[0x32C + i]);
        return contentId.ToString() + "4D";
    }

    private static void WriteU32BE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private class FileEntry
    {
        public string Name { get; init; } = "";
        public byte[] Data { get; init; } = [];
        public int StartBlock { get; set; }
        public int BlockCount { get; set; }
    }
}
