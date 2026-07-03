using System.Xml.Serialization;

namespace LipsSongExtractor.Poco;

[XmlRoot("ixb")]
public class Ixb
{
    [XmlAttribute("IsBigEndian")] public bool IsBigEndian { get; set; }
    [XmlAttribute("IsText")]      public bool IsText { get; set; }
    [XmlAttribute("Platform")]    public string Platform { get; set; }
    [XmlAttribute("NumOfElements")] public int NumOfElements { get; set; }

    [XmlElement("Classes")]
    public ClassesContainer Classes { get; set; } = new();

    internal static Ixb ReadHeader(string filePath, out long binaryStartPos)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var sr = new StreamReader(fs, leaveOpen: true);

        var headerBuilder = new System.Text.StringBuilder();
        binaryStartPos = 0;

        while (sr.ReadLine() is { } line)
        {
            headerBuilder.AppendLine(line);
            if (!line.Trim().EndsWith("</ixb>", StringComparison.OrdinalIgnoreCase)) continue;

            binaryStartPos = fs.Position;
            break;
        }

        if (binaryStartPos == 0)
            throw new InvalidDataException("Kein </ixb>-Tag gefunden – ungültige .x360‑Datei.");

        var serializer = new XmlSerializer(typeof(Ixb));
        using var reader = new StringReader(headerBuilder.ToString());
        var header = (Ixb)serializer.Deserialize(reader)!;
        return header;
    }

    internal static void ResolveInheritance(Ixb header)
    {
        var classes = header.Classes.ClassList;

        foreach (var cls in classes)
        {
            if (cls.Base <= 0) continue;
            var parentIdx = cls.Base - 1;
            if (parentIdx < 0 || parentIdx >= classes.Count)
                throw new InvalidOperationException(
                    $"Ungültiger Base‑Index {cls.Base} in Klasse {cls.Name}");

            cls.Parent = classes[parentIdx];
        }
    }
}