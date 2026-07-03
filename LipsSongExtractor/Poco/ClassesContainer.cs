using System.Xml.Serialization;

namespace LipsSongExtractor.Poco;

public class ClassesContainer
{
    [XmlElement("Class")]
    public List<ClassDef> ClassList { get; set; } = [];
}