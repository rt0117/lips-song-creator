using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Baut einen IXB Binary Blob from scratch auf.
///
/// Blob-Format (verifiziert gegen Original-DLCs, Happy Ending):
///   Jeder Eintrag: [classIndex:4][runtime_ptr:4][size:4][data:size]
///   - classIndex = 0            fuer Inline-Daten (Strings, Arrays)
///   - classIndex = 1-basierter  Index in die &lt;Classes&gt;-Liste fuer Objekte
///
/// Der Loader liest NumOfElements Eintraege sequentiell und instanziiert
/// Objekte anhand des Klassen-Index. Pointer werden in einem zweiten
/// Durchlauf aufgeloest (Forward-Referenzen sind erlaubt).
/// Das Root-ixPackage MUSS der letzte Eintrag sein.
/// </summary>
public class IxbBlobBuilder
{
    private readonly List<BlobEntry> _entries = [];
    private uint _nextPtr = 0x10000000; // Start-Pointer (beliebig, muss nur > 0 sein)

    /// <summary>
    /// Registriert einen Inline-String und gibt den Pointer zurueck.
    /// </summary>
    public uint AddString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        return AddInlineData(bytes);
    }

    /// <summary>
    /// Registriert rohe Inline-Daten (classIndex=0) und gibt den Pointer zurueck.
    /// </summary>
    public uint AddInlineData(byte[] data)
    {
        var ptr = _nextPtr;
        _nextPtr += 0x100; // Genug Abstand fuer eindeutige Pointer

        _entries.Add(new BlobEntry
        {
            Ptr = ptr,
            Data = data,
            ClassIndex = 0
        });

        return ptr;
    }

    /// <summary>
    /// Registriert ein Pointer-Array (z.B. fuer ixVector&lt;T*&gt;) und gibt den Pointer zurueck.
    /// </summary>
    public uint AddPointerArray(uint[] pointers)
    {
        var data = new byte[pointers.Length * 4];
        for (var i = 0; i < pointers.Length; i++)
            WriteBE(data, i * 4, pointers[i]);
        return AddInlineData(data);
    }

    /// <summary>
    /// Registriert ein Objekt mit Klassen-Index (1-basiert, Position in der
    /// &lt;Classes&gt;-Liste des XML-Headers) und fixer Groesse.
    /// </summary>
    public ObjectWriter AddObject(int classIndex, int classSize)
    {
        var ptr = _nextPtr;
        _nextPtr += 0x100;

        var data = new byte[classSize];
        _entries.Add(new BlobEntry
        {
            Ptr = ptr,
            Data = data,
            ClassIndex = classIndex
        });

        return new ObjectWriter(data, ptr);
    }

    /// <summary>
    /// Baut den fertigen Blob zusammen. Eintraege in Registrierungs-Reihenfolge:
    /// [classIndex:4][ptr:4][size:4][data] ...
    /// </summary>
    public byte[] Build()
    {
        var ms = new MemoryStream();

        foreach (var e in _entries)
        {
            WriteBE(ms, (uint)e.ClassIndex);
            WriteBE(ms, e.Ptr);
            WriteBE(ms, (uint)e.Data.Length);
            ms.Write(e.Data);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Anzahl der registrierten Eintraege (fuer NumOfElements im Header).
    /// </summary>
    public int EntryCount => _entries.Count;

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static void WriteBE(Stream s, uint value)
    {
        s.WriteByte((byte)(value >> 24));
        s.WriteByte((byte)(value >> 16));
        s.WriteByte((byte)(value >> 8));
        s.WriteByte((byte)value);
    }

    private class BlobEntry
    {
        public uint Ptr { get; init; }
        public byte[] Data { get; init; } = [];
        public int ClassIndex { get; init; }
    }
}

/// <summary>
/// Schreibt Felder in ein Objekt-Byte-Array an den korrekten Offsets.
/// Alle Werte werden Big-Endian geschrieben.
/// </summary>
public class ObjectWriter
{
    private readonly byte[] _data;
    public uint Ptr { get; }

    internal ObjectWriter(byte[] data, uint ptr)
    {
        _data = data;
        Ptr = ptr;
    }

    public void WriteU32(int offset, uint value)
    {
        _data[offset] = (byte)(value >> 24);
        _data[offset + 1] = (byte)(value >> 16);
        _data[offset + 2] = (byte)(value >> 8);
        _data[offset + 3] = (byte)value;
    }

    public void WriteI32(int offset, int value) => WriteU32(offset, (uint)value);

    public void WriteF32(int offset, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        Array.Copy(bytes, 0, _data, offset, 4);
    }

    /// <summary>
    /// Schreibt eine ixVector-Struktur: [_data:ptr][_reserve:4][_size:4][_allocator:0]
    /// </summary>
    public void WriteVector(int offset, uint dataPtr, int count, int reserve = 0)
    {
        WriteU32(offset, dataPtr);
        WriteU32(offset + 4, (uint)(reserve > 0 ? reserve : count));
        WriteU32(offset + 8, (uint)count);
        WriteU32(offset + 12, 0);
    }

    /// <summary>
    /// Schreibt einen ixVector&lt;char&gt; (String-Referenz).
    /// </summary>
    public void WriteStringVector(int offset, uint stringPtr, int stringLength, int reserve = 0)
    {
        WriteVector(offset, stringPtr, stringLength, reserve > 0 ? reserve : stringLength);
    }

    /// <summary>
    /// Schreibt rohe Bytes an einen Offset.
    /// </summary>
    public void WriteData(int offset, byte[] data)
    {
        Array.Copy(data, 0, _data, offset, data.Length);
    }
}
