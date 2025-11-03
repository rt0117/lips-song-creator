using System.Xml.Serialization;

namespace LipsSongExtractor.Poco;

public class MemberDef
{
    [XmlAttribute("Name")] public string Name { get; set; }
    [XmlAttribute("Offset")] public int Offset { get; set; }

    [XmlAttribute("Type")] public string Type { get; set; }
}