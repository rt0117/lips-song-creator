using System.Text;
using System.Xml.Serialization;
using LipsSongExtractor.Poco;

namespace LipsSongExtractor;

public static class X360Reader
{
    /// <summary>
    /// Liest eine .X360-Datei: XML-Header + Binary Blob.
    /// </summary>
    public static (Ixb header, byte[] binaryBlob) ReadFile(string path)
    {
        var allBytes = File.ReadAllBytes(path);

        var openTag = Encoding.ASCII.GetBytes("<Objects>");
        var closeTag = Encoding.ASCII.GetBytes("</Objects>");

        var objStart = IndexOf(allBytes, openTag);
        var objEnd = IndexOf(allBytes, closeTag);

        if (objStart < 0 || objEnd < 0 || objEnd <= objStart)
            throw new InvalidDataException(
                "Kann <Objects> ... </Objects> nicht im .x360-File finden.");

        var headerXml = Encoding.UTF8.GetString(allBytes, 0, objStart);
        headerXml += "</ixb>";

        var ser = new XmlSerializer(typeof(Ixb));
        Ixb header;
        using (var sr = new StringReader(headerXml))
            header = (Ixb)ser.Deserialize(sr)!;

        Ixb.ResolveInheritance(header);

        var blobStart = objStart + openTag.Length;
        var blobLen = objEnd - blobStart;
        var binaryBlob = new byte[blobLen];
        Buffer.BlockCopy(allBytes, blobStart, binaryBlob, 0, blobLen);

        return (header, binaryBlob);
    }

    /// <summary>
    /// Liest ein Objekt einer bestimmten Klasse aus dem Binary Blob.
    /// Gibt ein Dictionary mit Feldname -> typisiertem Wert zurück.
    /// Pointer werden rekursiv aufgelöst, Strings werden gelesen,
    /// Vektoren/Listen werden als List&lt;object&gt; zurückgegeben.
    /// </summary>
    internal static Dictionary<string, object?> ReadObject(
        ClassDef cls,
        ReadOnlySpan<byte> buffer,
        bool isBigEndian,
        Dictionary<string, ClassDef> classLookup,
        int depth = 0)
    {
        var dict = new Dictionary<string, object?>();

        // Metadaten: Welche Klasse und Offset im Blob
        dict["__class"] = cls.Name;

        foreach (var mem in cls.AllMembers)
        {
            var fieldSize = FieldSizes.DetermineFieldSize(mem.Type);

            // Bounds check
            if (mem.Offset + fieldSize > buffer.Length)
            {
                dict[mem.Name] = $"<out-of-bounds: offset={mem.Offset}, size={fieldSize}, bufLen={buffer.Length}>";
                continue;
            }

            var raw = buffer.Slice(mem.Offset, fieldSize).ToArray();

            // 1) Pointer zu einem Objekt (Typ endet auf *)
            if (mem.Type != null && mem.Type.Trim().EndsWith("*") &&
                !mem.Type.Contains("char*", StringComparison.OrdinalIgnoreCase) &&
                !mem.Type.StartsWith("ixVector", StringComparison.OrdinalIgnoreCase) &&
                !mem.Type.StartsWith("ixList", StringComparison.OrdinalIgnoreCase) &&
                !mem.Type.StartsWith("ixArray", StringComparison.OrdinalIgnoreCase))
            {
                var ptr = FieldSizes.ReadBE<uint>(raw);
                if (ptr == 0)
                {
                    dict[mem.Name] = null;
                    continue;
                }

                var targetName = mem.Type.Trim().TrimEnd('*').Trim();

                if (classLookup.TryGetValue(targetName, out var targetCls) && depth < 10)
                {
                    if ((int)ptr + targetCls.Size <= buffer.Length)
                    {
                        var subObj = ReadObject(targetCls, buffer[(int)ptr..],
                            isBigEndian, classLookup, depth + 1);
                        dict[mem.Name] = subObj;
                    }
                    else
                    {
                        dict[mem.Name] = $"<ptr:0x{ptr:X} out-of-bounds for {targetName}>";
                    }
                }
                else
                {
                    dict[mem.Name] = $"<ptr:0x{ptr:X} -> {targetName}>";
                }

                continue;
            }

            // 2) char* -> String
            if (mem.Type != null &&
                mem.Type.Contains("char*", StringComparison.InvariantCultureIgnoreCase))
            {
                var ptr = FieldSizes.ReadBE<uint>(raw);
                var str = FieldSizes.ReadCString(buffer, ptr, isBigEndian);
                dict[mem.Name] = str;
                continue;
            }

            // 3) ixVector<> String (char-Vektor) -> lese als String
            if (mem.Type != null && IsStringVector(mem.Type))
            {
                var dataPtr = FieldSizes.ReadBE<uint>(raw);
                var size = FieldSizes.ReadBE<int>(buffer.Slice(mem.Offset + 8, 4).ToArray());

                if (dataPtr != 0 && size > 0 && (int)dataPtr + size <= buffer.Length)
                {
                    var str = Encoding.UTF8.GetString(
                        buffer.Slice((int)dataPtr, size).ToArray()).TrimEnd('\0');
                    dict[mem.Name] = str;
                }
                else
                {
                    dict[mem.Name] = "";
                }

                continue;
            }

            // 4) ixVector / ixList / ixArray -> Container
            if (mem.Type != null && (mem.Type.StartsWith("ixVector") ||
                                     mem.Type.StartsWith("ixList") ||
                                     mem.Type.StartsWith("ixArray")))
            {
                var dataPtr = FieldSizes.ReadBE<uint>(raw);
                var reserve = FieldSizes.ReadBE<int>(buffer.Slice(mem.Offset + 4, 4).ToArray());
                var size = FieldSizes.ReadBE<int>(buffer.Slice(mem.Offset + 8, 4).ToArray());
                var allocator = FieldSizes.ReadBE<uint>(buffer.Slice(mem.Offset + 12, 4).ToArray());

                var elementType = ExtractTemplateArgument(mem.Type);
                var isPointerElement = elementType.EndsWith("*");
                var elementBase = elementType.TrimEnd('*').Trim();

                var elements = new List<object?>();
                if (dataPtr != 0 && size > 0)
                {
                    if (classLookup.TryGetValue(elementBase, out var elemCls))
                    {
                        if (isPointerElement)
                        {
                            // Array von Pointern (je 4 Bytes)
                            for (var i = 0; i < size; i++)
                            {
                                var ptrOff = (int)dataPtr + i * 4;
                                if (ptrOff + 4 > buffer.Length) break;
                                var elemPtr = FieldSizes.ReadBE<uint>(
                                    buffer.Slice(ptrOff, 4).ToArray());
                                if (elemPtr == 0 || (int)elemPtr + elemCls.Size > buffer.Length)
                                {
                                    elements.Add(null);
                                    continue;
                                }

                                if (depth < 10)
                                {
                                    var elem = ReadObject(elemCls, buffer[(int)elemPtr..],
                                        isBigEndian, classLookup, depth + 1);
                                    elements.Add(elem);
                                }
                                else
                                {
                                    elements.Add($"<ptr:0x{elemPtr:X} -> {elementBase} (max depth)>");
                                }
                            }
                        }
                        else
                        {
                            // Inline-Array von Structs
                            var elemSize = elemCls.Size;
                            for (var i = 0; i < size; i++)
                            {
                                var elemOff = (int)dataPtr + i * elemSize;
                                if (elemOff + elemSize > buffer.Length) break;
                                if (depth < 10)
                                {
                                    var elem = ReadObject(elemCls, buffer[elemOff..],
                                        isBigEndian, classLookup, depth + 1);
                                    elements.Add(elem);
                                }
                                else
                                {
                                    elements.Add($"<inline {elementBase} at 0x{elemOff:X} (max depth)>");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Primitiver Elementtyp (unsigned int, etc.)
                        var primSize = FieldSizes.DetermineFieldSize(elementBase);
                        for (var i = 0; i < size; i++)
                        {
                            var elemOff = (int)dataPtr + i * primSize;
                            if (elemOff + primSize > buffer.Length) break;
                            var elemRaw = buffer.Slice(elemOff, primSize).ToArray();
                            elements.Add(FieldSizes.ConvertByType(elementBase, "", elemRaw, isBigEndian));
                        }
                    }
                }

                dict[mem.Name] = new VectorInfo
                {
                    ElementType = elementType,
                    Count = size,
                    Reserve = reserve,
                    Elements = elements
                };
                continue;
            }

            // 5) Primitiver Typ - typisiert konvertieren
            dict[mem.Name] = FieldSizes.ConvertByType(mem.Type, mem.Name, raw, isBigEndian);
        }

        return dict;
    }

    /// <summary>
    /// Prüft ob ein ixVector-Typ ein String-Vektor ist (ixVector&lt;char,...&gt;).
    /// </summary>
    private static bool IsStringVector(string type)
    {
        if (!type.StartsWith("ixVector")) return false;
        var inner = ExtractTemplateArgument(type);
        return inner.Equals("char", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extrahiert den Template-Parameter aus einem Typ wie "ixVector&lt;Foo*,1,...&gt;".
    /// </summary>
    private static string ExtractTemplateArgument(string type)
    {
        var lt = type.IndexOf('<');
        var gt = type.LastIndexOf('>');
        if (lt < 0 || gt < 0) return string.Empty;

        var inner = type.Substring(lt + 1, gt - lt - 1);
        var comma = inner.IndexOf(',');
        if (comma > 0) inner = inner[..comma];
        return inner.Trim();
    }

    private static int IndexOf(byte[] source, byte[] pattern)
    {
        if (pattern.Length == 0) return 0;
        for (var i = 0; i <= source.Length - pattern.Length; i++)
        {
            var match = !pattern.Where((t, j) => source[i + j] != t).Any();
            if (match) return i;
        }

        return -1;
    }
}

/// <summary>
/// Container-Info für ixVector/ixList/ixArray - mit Metadaten und typisierten Elementen.
/// </summary>
public class VectorInfo
{
    public string ElementType { get; set; } = "";
    public int Count { get; set; }
    public int Reserve { get; set; }
    public List<object?> Elements { get; set; } = [];
}
