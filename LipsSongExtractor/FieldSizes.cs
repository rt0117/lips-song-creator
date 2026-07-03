using System.Text;

namespace LipsSongExtractor;

public static class FieldSizes
{
    /// <summary>
    /// Bestimmt die Byte-Größe eines Feldes anhand des Typ-Strings aus dem XML-Header.
    /// </summary>
    internal static int DetermineFieldSize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return 4;

        var t = type.Trim().ToLowerInvariant();

        // Pointer-Typen (4 Bytes auf Xbox 360)
        if (t.EndsWith("*") || t.Contains("pointer"))
            return 4;

        // Container-Typen
        if (t.StartsWith("ixvector") ||
            t.StartsWith("ixlist") ||
            t.StartsWith("ixarray"))
            return 16;

        // Primitive Typen
        if (t == "bool" || t == "unsigned char" || t == "char" || t == "byte")
            return 1;
        if (t.Contains("char") && !t.Contains("*"))
            return 1;
        if (t == "short" || t == "unsigned short" || t == "ushort")
            return 2;
        if (t.Contains("short"))
            return 2;
        if (t == "float")
            return 4;
        if (t.Contains("float"))
            return 4;
        if (t == "int" || t == "unsigned int" || t == "uint")
            return 4;
        if (t.Contains("int"))
            return 4;
        if (t == "double" || t == "long" || t == "unsigned long")
            return 8;
        if (t.Contains("double") || t.Contains("long"))
            return 8;
        if (t.Contains("bool"))
            return 1;

        return 4;
    }

    /// <summary>
    /// Konvertiert ein Feld anhand des XML-Header-Typs in den richtigen .NET-Typ.
    /// Gibt int, uint, float, bool, string etc. zurück - keine rohen Bytes mehr.
    /// </summary>
    internal static object ConvertByType(string? type, string name, byte[] raw, bool isBigEndian)
    {
        if (string.IsNullOrWhiteSpace(type))
            return ConvertByName(name, raw);

        var t = type.Trim().ToLowerInvariant();

        // Bool
        if (t == "bool" || t.Contains("bool"))
            return raw[0] != 0;

        // Byte / unsigned char
        if (t == "unsigned char" || t == "byte")
            return raw[0];

        // Char (einzeln)
        if (t == "char")
            return (char)raw[0];

        // Short
        if (t.Contains("unsigned short") || t.Contains("ushort"))
            return ReadBE<ushort>(raw);
        if (t.Contains("short"))
            return ReadBE<short>(raw);

        // Float
        if (t.Contains("float"))
            return ReadBE<float>(raw);

        // Double
        if (t.Contains("double"))
            return ReadBE<double>(raw);

        // Long
        if (t.Contains("unsigned long") || t.Contains("ulong"))
            return ReadBE<ulong>(raw);
        if (t.Contains("long"))
            return ReadBE<long>(raw);

        // Unsigned int
        if (t.Contains("unsigned int") || t.Contains("uint"))
            return ReadBE<uint>(raw);

        // Int
        if (t.Contains("int"))
            return ReadBE<int>(raw);

        // Fallback: Name-basierte Heuristik
        return ConvertByName(name, raw);
    }

    /// <summary>
    /// Fallback-Konvertierung basierend auf dem Feldnamen (ungarische Notation).
    /// </summary>
    private static object ConvertByName(string name, byte[] raw)
    {
        if (raw.Length == 1)
        {
            if (name.Length == 1 && "rgba".Contains(name[0]))
                return raw[0];
            if (name.StartsWith("m_b") || name.StartsWith("b"))
                return raw[0] != 0;
            return raw[0];
        }

        if (raw.Length == 4)
        {
            if (name.StartsWith("m_p") || name.StartsWith("_data") || name.StartsWith("_root"))
                return ReadBE<uint>(raw);
            if (name.StartsWith("m_f") || name.StartsWith("f"))
                return ReadBE<float>(raw);
            if (name.StartsWith("m_ui") || name.StartsWith("m_u") ||
                name.StartsWith("Uint") || name.StartsWith("ID") ||
                name.StartsWith("_reserve") || name.StartsWith("_size") ||
                name.StartsWith("_allocator"))
                return ReadBE<uint>(raw);
            if (name.StartsWith("m_i") || name.StartsWith("i") || name.StartsWith("m_n"))
                return ReadBE<int>(raw);

            // Default für 4-Byte: uint
            return ReadBE<uint>(raw);
        }

        if (raw.Length == 8)
        {
            if (name.StartsWith("m_d") || name.StartsWith("d"))
                return ReadBE<double>(raw);
            return ReadBE<long>(raw);
        }

        if (raw.Length == 2)
            return ReadBE<ushort>(raw);

        return raw;
    }

    /// <summary>
    /// Liest einen Big-Endian-Wert aus einem Byte-Array.
    /// Erstellt eine Kopie, damit das Original nicht verändert wird.
    /// </summary>
    internal static T ReadBE<T>(byte[] raw) where T : struct
    {
        var copy = (byte[])raw.Clone();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(copy);

        if (typeof(T) == typeof(int)) return (T)(object)BitConverter.ToInt32(copy, 0);
        if (typeof(T) == typeof(uint)) return (T)(object)BitConverter.ToUInt32(copy, 0);
        if (typeof(T) == typeof(short)) return (T)(object)BitConverter.ToInt16(copy, 0);
        if (typeof(T) == typeof(ushort)) return (T)(object)BitConverter.ToUInt16(copy, 0);
        if (typeof(T) == typeof(long)) return (T)(object)BitConverter.ToInt64(copy, 0);
        if (typeof(T) == typeof(ulong)) return (T)(object)BitConverter.ToUInt64(copy, 0);
        if (typeof(T) == typeof(float)) return (T)(object)BitConverter.ToSingle(copy, 0);
        if (typeof(T) == typeof(double)) return (T)(object)BitConverter.ToDouble(copy, 0);
        if (typeof(T) == typeof(bool)) return (T)(object)(copy[0] != 0);
        if (typeof(T) == typeof(byte)) return (T)(object)copy[0];

        throw new NotSupportedException($"Typ {typeof(T)} wird nicht unterstützt.");
    }

    /// <summary>
    /// Liest einen nullterminierten UTF-8-String aus dem Buffer ab Position ptr.
    /// </summary>
    internal static string ReadCString(ReadOnlySpan<byte> buffer, uint ptr, bool isBigEndian)
    {
        if (ptr == 0) return string.Empty;

        var start = (int)ptr;
        if (start >= buffer.Length) return $"<invalid-ptr:0x{ptr:X}>";

        var end = start;
        while (end < buffer.Length && buffer[end] != 0) end++;

        var length = end - start;
        return Encoding.UTF8.GetString(buffer.Slice(start, length));
    }

    // Behalte die alte Methode als Alias für Abwärtskompatibilität
    [Obsolete("Use ReadBE<T> instead")]
    internal static T FromBigEndian<T>(byte[] raw) where T : struct => ReadBE<T>(raw);
}
