using LipsSongExtractor;

if (args.Length < 2)
{
    Console.WriteLine("Aufruf: LipsSongExtractor.exe <PfadZur.X360> <KlassenName>");
    return;
}

var filePath = args[0];
var wantedClass = args[1];

var (header, binaryBlob) = X360Reader.ReadFile(filePath);

var cls = header.Classes.ClassList.Find(c => c.Name == wantedClass);
if (cls == null)
{
    Console.WriteLine($"Klasse '{wantedClass}' nicht gefunden.");
    return;
}

var classLookup = header.Classes.ClassList.ToDictionary(c => c.Name);

var obj = X360Reader.ReadObject(cls, binaryBlob, header.IsBigEndian, classLookup);

Console.WriteLine($"--- {cls.Name} (Size={cls.Size}) ---");
foreach (var kv in obj)
{
    var raw = (byte[])kv.Value!;
    Console.WriteLine($"{kv.Key,-30} Size={raw.Length}  Data={BitConverter.ToString(raw)}");
}