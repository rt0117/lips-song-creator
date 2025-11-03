using System.Text;
using System.Xml.Serialization;
using LipsSongExtractor.Poco;

namespace LipsSongExtractor;

public static class X360Reader
{
    public static (Ixb header, byte[] binaryBlob) ReadFile(string path)
    {
        var allBytes = File.ReadAllBytes(path);
        
        var openTag = Encoding.ASCII.GetBytes("<Objects>");
        var closeTag = Encoding.ASCII.GetBytes("</Objects>");

        var objStart = IndexOf(allBytes, openTag);
        var objEnd = IndexOf(allBytes, closeTag);

        if (objStart < 0 || objEnd < 0 || objEnd <= objStart)
            throw new InvalidDataException(
                "Kann <Objects> … </Objects> nicht im .x360‑File finden.");
        
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

    internal static Dictionary<string, object?> ReadObject(
    ClassDef cls,
    ReadOnlySpan<byte> buffer,
    bool isBigEndian,
    Dictionary<string, ClassDef> classLookup)
{
    var dict = new Dictionary<string, object?>();

    foreach (var mem in cls.AllMembers)
    {
        var fieldSize = FieldSizes.DetermineFieldSize(mem.Type);
        var raw = buffer.Slice(mem.Offset, fieldSize).ToArray();
        
        if (mem.Type != null && mem.Type.Trim().EndsWith("*"))
        {
            var ptr = FieldSizes.FromBigEndian<uint>(raw);
            if (ptr == 0)
            {
                dict[mem.Name] = null;
                continue;
            }
            
            var targetName = mem.Type.Trim().TrimEnd('*').Trim();

            if (!classLookup.TryGetValue(targetName, out var targetCls))
                throw new InvalidOperationException(
                    $"Unbekannte Zielklasse für Zeiger {mem.Name}: {targetName}");
            
            var subObj = ReadObject(targetCls,
                                   buffer[(int)ptr..],
                                   isBigEndian,
                                   classLookup);
            dict[mem.Name] = subObj;
            continue;
        }
        
        if (mem.Type != null && mem.Type.Contains("char*", StringComparison.InvariantCultureIgnoreCase))
        {
            var ptr = FieldSizes.FromBigEndian<uint>(raw);
            var str = FieldSizes.ReadCString(buffer, ptr, isBigEndian);
            dict[mem.Name] = str;
            continue;
        }
        if (mem.Type != null && (mem.Type.StartsWith("ixVector") ||
                                 mem.Type.StartsWith("ixList")   ||
                                 mem.Type.StartsWith("ixArray")))
        {
            var dataPtr   = FieldSizes.FromBigEndian<uint>(raw);                    
            var  reserve  = FieldSizes.FromBigEndian<int>(buffer.Slice(mem.Offset + 4, 4).ToArray());
            var  size     = FieldSizes.FromBigEndian<int>(buffer.Slice(mem.Offset + 8, 4).ToArray());
            var allocator = FieldSizes.FromBigEndian<uint>(buffer.Slice(mem.Offset + 12, 4).ToArray());

            var elementType = ExtractTemplateArgument(mem.Type);
            var elementBase = elementType.TrimEnd('*').Trim(); // falls Pointer

            var elements = new List<object?>();
            if (dataPtr != 0 && size > 0 && classLookup.TryGetValue(elementBase, out var elemCls))
            {
                var elemSize = elemCls.Size; // Gesamtsize inkl. Vererbung
                for (var i = 0; i < size; i++)
                {
                    var elemOff = (int)dataPtr + i * elemSize;
                    var elem = ReadObject(elemCls,
                                          buffer.Slice(elemOff),
                                          isBigEndian,
                                          classLookup);
                    elements.Add(elem);
                }
            }
            dict[mem.Name] = elements; 
            continue;
        }

        dict[mem.Name] = FieldSizes.ConvertMember(mem.Name, raw, isBigEndian);
    }

    return dict;
}

    private static string ExtractTemplateArgument(string type)
    {
        // Example: "ixVector<ixAsset *,ixAllocator<ixAsset* ,1>>"
        var lt = type.IndexOf('<');
        var gt = type.LastIndexOf('>');
        if (lt < 0 || gt < 0) return string.Empty;

        var inner = type.Substring(lt + 1, gt - lt - 1);
        var comma = inner.IndexOf(',');
        if (comma > 0) inner = inner[..comma];
        return inner.Trim();
    }
}