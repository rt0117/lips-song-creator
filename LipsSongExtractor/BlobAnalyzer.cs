using System.Text;
using LipsSongExtractor.Poco;

namespace LipsSongExtractor;

/// <summary>
/// Analysiert den Binary Blob, um das IXB-Speicherlayout zu verstehen.
/// </summary>
public static class BlobAnalyzer
{
    public static uint RU32(byte[] b, int o) =>
        (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    public static int RI32(byte[] b, int o) => (int)RU32(b, o);

    public static float RF32(byte[] b, int o)
    {
        var tmp = new byte[] { b[o + 3], b[o + 2], b[o + 1], b[o] };
        return BitConverter.ToSingle(tmp, 0);
    }

    public static string ReadStr(byte[] b, int o, int maxLen = 256)
    {
        if (o < 0 || o >= b.Length) return "<invalid>";
        var end = o;
        while (end < b.Length && end - o < maxLen && b[end] != 0) end++;
        return Encoding.UTF8.GetString(b, o, end - o);
    }

    /// <summary>
    /// Hauptanalyse: Parst den Blob als [ptr:4][size:4][data:size]-Einträge
    /// und interpretiert die Objekte anhand der Klassen-Definitionen.
    /// </summary>
    public static void AnalyzeBlob(byte[] blob, Ixb header)
    {
        Console.WriteLine($"=== Blob Analyse ===");
        Console.WriteLine($"Blob-Groesse: {blob.Length} Bytes (0x{blob.Length:X})");
        Console.WriteLine($"Objekt-Anzahl laut Header: {header.NumOfElements}");
        Console.WriteLine();

        // Scanne den Blob sequentiell als [ptr:4][size:4][data:size] Einträge
        Console.WriteLine("--- Sequentielle Eintraege [ptr:4][size:4][data:size] ---");
        ScanEntries(blob, header);

        Console.WriteLine();
        Console.WriteLine("--- Objekt-Klassen-Zuordnung ---");
        MapEntriesToClasses(blob, header);
    }

    /// <summary>
    /// Scannt den Blob nach [ptr:4][size:4][data:size] Einträgen.
    /// </summary>
    private static void ScanEntries(byte[] blob, Ixb header)
    {
        // Alle möglichen Einträge finden
        var entries = FindAllEntries(blob);

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var preview = MakePreview(blob, e.DataOffset, e.Size, 60);
            var classMatch = FindClassBySize(header, e.Size);
            var classInfo = classMatch != null ? $" -> {classMatch.Name}" : "";
            Console.WriteLine(
                $"  [{i,3}] off=0x{e.Offset:X4} ptr=0x{e.Ptr:X8} size={e.Size,6}{classInfo}");
            if (preview.Length > 0)
                Console.WriteLine($"        data='{preview}'");
        }

        Console.WriteLine($"  Total: {entries.Count} Eintraege");
    }

    /// <summary>
    /// Findet alle [ptr:4][size:4][data:size] Einträge im Blob.
    /// </summary>
    public static List<BlobEntry> FindAllEntries(byte[] blob)
    {
        var entries = new List<BlobEntry>();

        // Scanne JEDE 4-Byte-aligned Position als potenziellen Entry-Start
        for (var off = 0; off < blob.Length - 8; off++)
        {
            var ptr = RU32(blob, off);
            var size = RU32(blob, off + 4);

            // Sanity-Checks
            if (size == 0 || size > (uint)(blob.Length - off - 8)) continue;
            if (ptr == 0) continue; // Null-Pointer = kein gültiger Entry

            // Prüfe ob nach dem Eintrag ein weiterer gültiger Eintrag folgt
            // oder ob wir am Ende des Blobs sind
            var dataEnd = off + 8 + (int)size;
            if (dataEnd > blob.Length) continue;

            entries.Add(new BlobEntry
            {
                Offset = off,
                Ptr = ptr,
                Size = (int)size,
                DataOffset = off + 8
            });
        }

        return entries;
    }

    /// <summary>
    /// Versucht den Blob rückwärts vom Ende her zu parsen.
    /// Am Ende liegen die Objekte mit fixem Layout.
    /// </summary>
    private static void MapEntriesToClasses(byte[] blob, Ixb header)
    {
        var classBySize = new Dictionary<int, List<ClassDef>>();
        foreach (var cls in header.Classes.ClassList)
        {
            if (!classBySize.ContainsKey(cls.Size))
                classBySize[cls.Size] = [];
            classBySize[cls.Size].Add(cls);
        }

        // Parse vom Ende rückwärts
        var pos = blob.Length;
        var objects = new List<(int offset, int size, ClassDef? cls, uint ptr)>();

        while (pos >= 8)
        {
            // Versuche: Der Eintrag vor pos hat size=X bei pos-X-8
            // und ptr bei pos-X-4
            var found = false;

            // Probiere alle bekannten Klassen-Größen
            foreach (var kvp in classBySize)
            {
                var trySize = kvp.Key;
                var tryStart = pos - trySize - 8;
                if (tryStart < 0) continue;

                var tryPtr = RU32(blob, tryStart);
                var tryLen = (int)RU32(blob, tryStart + 4);

                if (tryLen == trySize && tryPtr != 0)
                {
                    objects.Insert(0, (tryStart, trySize, kvp.Value[0], tryPtr));
                    pos = tryStart;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Versuche beliebige Größe
                for (var guessSize = 1; guessSize < Math.Min(pos, 10000); guessSize++)
                {
                    var tryStart = pos - guessSize - 8;
                    if (tryStart < 0) break;

                    var tryPtr = RU32(blob, tryStart);
                    var tryLen = (int)RU32(blob, tryStart + 4);

                    if (tryLen == guessSize && tryPtr != 0)
                    {
                        var cls = FindClassBySize(header, guessSize);
                        objects.Insert(0, (tryStart, guessSize, cls, tryPtr));
                        pos = tryStart;
                        found = true;
                        break;
                    }
                }

                if (!found) break;
            }
        }

        Console.WriteLine($"  Geparst vom Ende: {objects.Count} Objekte, verbleibend: {pos} Bytes am Anfang");
        Console.WriteLine();

        foreach (var (offset, size, cls, ptr) in objects)
        {
            var className = cls?.Name ?? $"<unknown size={size}>";
            Console.WriteLine($"  off=0x{offset:X4} ptr=0x{ptr:X8} size={size,5} class={className}");

            // Wenn es eine bekannte Klasse ist, Felder ausgeben
            if (cls != null)
            {
                var dataOff = offset + 8;
                PrintObjectFields(blob, dataOff, cls, header);
            }
        }
    }

    /// <summary>
    /// Gibt die Felder eines Objekts aus, basierend auf der Klassendefinition.
    /// Erkennt ixVector<char> anhand der Feldgröße (16 Bytes mit String-Daten).
    /// </summary>
    private static void PrintObjectFields(byte[] blob, int dataOffset, ClassDef cls, Ixb header)
    {
        var members = cls.AllMembers.OrderBy(m => m.Offset).ToList();

        for (var i = 0; i < members.Count; i++)
        {
            var mem = members[i];
            var fieldStart = dataOffset + mem.Offset;
            if (fieldStart + 4 > blob.Length) break;

            // Bestimme die Feldgröße
            var nextOffset = i + 1 < members.Count ? members[i + 1].Offset : cls.Size;
            var fieldSize = nextOffset - mem.Offset;

            if (fieldSize == 16 && mem.Name.Contains("str", StringComparison.OrdinalIgnoreCase) ||
                fieldSize == 16 && mem.Name.Contains("Name", StringComparison.OrdinalIgnoreCase))
            {
                // Wahrscheinlich ixVector<char> -> versuche String zu lesen
                var dataPtr = RU32(blob, fieldStart);
                var reserve = RU32(blob, fieldStart + 4);
                var size = RU32(blob, fieldStart + 8);
                var alloc = RU32(blob, fieldStart + 12);

                // Versuche ob dataPtr auf einen String im Blob zeigt
                // (Die Pointer sind Runtime-Adressen, wir müssen sie mappen)
                Console.WriteLine(
                    $"    {mem.Name,-30} ixVector<char>: _data=0x{dataPtr:X8} _res={reserve} _size={size} _alloc=0x{alloc:X8}");
            }
            else if (fieldSize == 4)
            {
                var val = RU32(blob, fieldStart);
                Console.WriteLine($"    {mem.Name,-30} = {val} (0x{val:X8})");
            }
            else if (fieldSize == 16)
            {
                // 16-byte Feld, könnte ixVector sein
                var p0 = RU32(blob, fieldStart);
                var p1 = RU32(blob, fieldStart + 4);
                var p2 = RU32(blob, fieldStart + 8);
                var p3 = RU32(blob, fieldStart + 12);
                Console.WriteLine(
                    $"    {mem.Name,-30} [16B]: 0x{p0:X8} {p1} {p2} 0x{p3:X8}");
            }
            else
            {
                var hex = new StringBuilder();
                for (var j = 0; j < Math.Min(fieldSize, 16); j++)
                {
                    if (fieldStart + j < blob.Length)
                        hex.AppendFormat("{0:X2} ", blob[fieldStart + j]);
                }

                Console.WriteLine($"    {mem.Name,-30} [{fieldSize}B]: {hex}");
            }
        }
    }

    private static ClassDef? FindClassBySize(Ixb header, int size)
    {
        return header.Classes.ClassList.FirstOrDefault(c => c.Size == size);
    }

    private static string MakePreview(byte[] blob, int offset, int size, int maxLen)
    {
        var sb = new StringBuilder();
        var len = Math.Min(size, maxLen);
        for (var i = 0; i < len && offset + i < blob.Length; i++)
        {
            var b = blob[offset + i];
            sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
        }

        if (size > maxLen) sb.Append("...");
        return sb.ToString();
    }

    public static void HexDump(byte[] blob, int offset, int length, int bytesPerLine = 16)
    {
        for (var i = 0; i < length; i += bytesPerLine)
        {
            var lineOff = offset + i;
            if (lineOff >= blob.Length) break;

            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (var j = 0; j < bytesPerLine; j++)
            {
                var idx = lineOff + j;
                if (idx >= blob.Length || i + j >= length)
                {
                    hex.Append("   ");
                    ascii.Append(' ');
                }
                else
                {
                    hex.AppendFormat("{0:X2} ", blob[idx]);
                    ascii.Append(blob[idx] >= 32 && blob[idx] <= 126 ? (char)blob[idx] : '.');
                }
            }

            Console.WriteLine($"{lineOff:X6}  {hex} |{ascii}|");
        }
    }
}

public class BlobEntry
{
    public int Offset { get; set; }
    public uint Ptr { get; set; }
    public int Size { get; set; }
    public int DataOffset { get; set; }
}
