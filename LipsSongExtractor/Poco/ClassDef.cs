using System.Xml.Serialization;

namespace LipsSongExtractor.Poco;

/// <summary>
/// Beschreibung einer einzelnen Klasse / Struktur
/// </summary>
public class ClassDef
{
    [XmlAttribute("Name")] public string Name { get; set; }
    [XmlAttribute("Base")] public int Base { get; set; }
    [XmlAttribute("Size")] public int Size { get; set; }
    [XmlArray("Members")]
    [XmlArrayItem("Member")]
    public List<MemberDef> Members { get; set; } = [];

    [XmlIgnore] public ClassDef Parent { get; set; }   // aufgelöste Basisklasse
    [XmlIgnore] public List<MemberDef> AllMembers => BuildAllMembers();

    private List<MemberDef> BuildAllMembers()
    {
        var list = new List<MemberDef>();
        if (Parent != null) list.AddRange(Parent.AllMembers);
        list.AddRange(Members);
        return list;
    }
}
