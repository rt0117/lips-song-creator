using System.Text;

namespace LipsSongExtractor;

public static class FieldSizes
{
    internal static int DetermineFieldSize(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return 4;

        var t = type.Trim().ToLowerInvariant();

        if (t.Contains("char") && !t.Contains("pointer"))
            return 1;
        if (t.Contains("short"))
            return 2;
        if (t.Contains("int") || t.Contains("float"))
            return 4;
        if (t.Contains("double") || t.Contains("long"))
            return 8;
        if (t.Contains("bool"))
            return 1;
        
        if (t.EndsWith("*") || t.Contains("pointer"))
            return 4;
        
        if (t.StartsWith("ixvector") ||
            t.StartsWith("ixlist") ||
            t.StartsWith("ixarray"))
            return 16;
        
        return 4;
    }
    
    internal static T FromBigEndian<T>(byte[] raw) where T : struct
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(raw);
        
        if (typeof(T) == typeof(int)) return (T)(object)BitConverter.ToInt32(raw, 0);
        if (typeof(T) == typeof(uint)) return (T)(object)BitConverter.ToUInt32(raw, 0);
        if (typeof(T) == typeof(short)) return (T)(object)BitConverter.ToInt16(raw, 0);
        if (typeof(T) == typeof(ushort)) return (T)(object)BitConverter.ToUInt16(raw, 0);
        if (typeof(T) == typeof(long)) return (T)(object)BitConverter.ToInt64(raw, 0);
        if (typeof(T) == typeof(ulong)) return (T)(object)BitConverter.ToUInt64(raw, 0);
        if (typeof(T) == typeof(float)) return (T)(object)BitConverter.ToSingle(raw, 0);
        if (typeof(T) == typeof(double)) return (T)(object)BitConverter.ToDouble(raw, 0);
        if (typeof(T) == typeof(bool)) return (T)(object)(raw[0] != 0);

        throw new NotSupportedException($"Typ {typeof(T)} wird nicht unterstützt.");
    }
    
    internal static object ConvertMember(string name, byte[] raw, bool isBigEndian)
    {
        if (name.StartsWith("m_p"))
            return FromBigEndian<uint>(raw);
        
        if (name.StartsWith("m_b") || name.StartsWith("b"))
            return FromBigEndian<bool>(raw);
        
        if (name.StartsWith("m_ui") || name.StartsWith("m_u") || name.StartsWith("Uint") || name.StartsWith("ID"))
            return FromBigEndian<uint>(raw);
        
        if (name.StartsWith("m_i") || name.StartsWith("i"))
            return FromBigEndian<int>(raw);
        
        if (name.StartsWith("m_f") || name.StartsWith("f"))
            return FromBigEndian<float>(raw);

        if (name.StartsWith("m_d") || name.StartsWith("d"))
            return FromBigEndian<double>(raw);

        if (name.Length == 1 && "rgba".Contains(name[0]))
            return raw[0];
        
        return raw;
    }
    
    internal static string ReadCString(ReadOnlySpan<byte> buffer, uint ptr, bool isBigEndian)
    {
        if (ptr == 0) return string.Empty;

        var start = (int)ptr;
        var end   = start;
        while (end < buffer.Length && buffer[end] != 0) end++;

        var length = end - start;
        return Encoding.UTF8.GetString(buffer.Slice(start, length));
    }
}