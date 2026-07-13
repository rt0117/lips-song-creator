using System.Text;
using System.Text.Json;
using LipsSongExtractor;
using LipsSongExtractor.Poco;

if (args.Length < 1)
{
    PrintUsage();
    return;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "classes":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad.X360>"); return; }
        CmdListClasses(args[1]);
        break;

    case "dump":
        if (args.Length < 3) { Console.WriteLine("Fehlt: <Pfad.X360> <Klasse>"); return; }
        CmdDump(args[1], args[2]);
        break;

    case "info":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad.X360>"); return; }
        CmdSongInfo(args[1]);
        break;

    case "chart":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad.X360>"); return; }
        CmdChart(args[1]);
        break;

    case "export-json":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad.X360>"); return; }
        var outPath = args.Length >= 3 ? args[2] : null;
        CmdExportJson(args[1], outPath);
        break;

    case "analyze":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad.X360>"); return; }
        CmdAnalyze(args[1]);
        break;

    case "hexdump":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad.X360>"); return; }
        var hexOffset = args.Length >= 3
            ? int.Parse(args[2], System.Globalization.NumberStyles.HexNumber)
            : 0;
        var hexLen = args.Length >= 4 ? int.Parse(args[3]) : 256;
        CmdHexDump(args[1], hexOffset, hexLen);
        break;

    case "stfs-list":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <Pfad-zum-STFS-Paket>"); return; }
        CmdStfsList(args[1]);
        break;

    case "stfs-extract":
        if (args.Length < 3) { Console.WriteLine("Fehlt: <Pfad-zum-STFS-Paket> <Ausgabe-Ordner>"); return; }
        CmdStfsExtract(args[1], args[2]);
        break;

    case "stfs-repack":
        if (args.Length < 4) { Console.WriteLine("Fehlt: <Template-DLC> <Song-Ordner> <Ausgabe>"); return; }
        CmdStfsRepack(args[1], args[2], args[3]);
        break;

    case "create-test-dlc":
        if (args.Length < 3)
        {
            Console.WriteLine("Fehlt: <Song-Ordner> <Ausgabe-Pfad>");
            Console.WriteLine("  Song-Ordner:  Ordner mit den Song-Dateien (.X360, .xWMA, .jpg, etc.)");
            Console.WriteLine("  Ausgabe-Pfad: Pfad fuer das erzeugte STFS-Paket");
            return;
        }
        CmdCreateTestDlc(args[1], args[2]);
        break;

    case "convert-ultrastar":
        if (args.Length < 3) { Console.WriteLine("Fehlt: <UltraStar.txt> <Ausgabe-Ordner>"); return; }
        CmdConvertUltraStar(args[1], args[2]);
        break;

    case "dump-db":
        if (args.Length < 2) { Console.WriteLine("Fehlt: <GameContentDB-Pfad>"); return; }
        CmdDumpDb(args[1]);
        break;

    default:
        Console.WriteLine($"Unbekannter Befehl: {command}");
        PrintUsage();
        break;
}

// ──────────────────────────────────────────────────────────────
// Befehle
// ──────────────────────────────────────────────────────────────

void PrintUsage()
{
    Console.WriteLine("LipsSongExtractor - Lips (.X360) Reverse Engineering Tool");
    Console.WriteLine();
    Console.WriteLine("Befehle:");
    Console.WriteLine("  classes    <Pfad.X360>                   Alle Klassen im Header auflisten");
    Console.WriteLine("  dump       <Pfad.X360> <Klasse>          Objekt einer Klasse ausgeben");
    Console.WriteLine("  info       <Pfad.X360>                   Song-Metadaten anzeigen");
    Console.WriteLine("  chart      <Pfad.X360>                   Chart analysieren (Tempo, Noten, Lyrics)");
    Console.WriteLine("  export-json <Pfad.X360> [output.json]    Alles als JSON exportieren");
    Console.WriteLine("  stfs-list  <Paket>                       STFS/LIVE-Paket: Dateien auflisten");
    Console.WriteLine("  stfs-extract <Paket> <Ordner>            STFS/LIVE-Paket: Dateien extrahieren");
    Console.WriteLine("  create-test-dlc <Song-Ordner> <Ausgabe>     STFS DLC-Paket aus Song-Ordner erzeugen");
    Console.WriteLine("  convert-ultrastar <TXT> <Ausgabe-Ordner>  UltraStar -> Lips DLC konvertieren");
    Console.WriteLine("  analyze    <Pfad.X360>                   Blob-Struktur analysieren");
    Console.WriteLine("  hexdump    <Pfad.X360> [offset] [len]    Hex-Dump ab offset (hex)");
}

void CmdListClasses(string filePath)
{
    var (header, _) = X360Reader.ReadFile(filePath);
    Console.WriteLine($"Datei: {filePath}");
    Console.WriteLine(
        $"BigEndian={header.IsBigEndian}  Platform={header.Platform}  Objekte={header.NumOfElements}");
    Console.WriteLine();
    Console.WriteLine($"{"#",-4} {"Name",-60} {"Size",-6} {"Base",-6} {"Members",7}");
    Console.WriteLine(new string('-', 90));

    for (var i = 0; i < header.Classes.ClassList.Count; i++)
    {
        var c = header.Classes.ClassList[i];
        var baseName = c.Parent != null ? c.Parent.Name : (c.Base > 0 ? $"#{c.Base}" : "-");
        Console.WriteLine(
            $"{i + 1,-4} {c.Name,-60} {c.Size,-6} {baseName,-6} {c.Members.Count,7}");
    }
}

void CmdAnalyze(string filePath)
{
    var (header, blob) = X360Reader.ReadFile(filePath);
    var deser = new IxbDeserializer(blob, header);
    deser.PrintSummary();
}

void CmdHexDump(string filePath, int offset, int length)
{
    var (_, blob) = X360Reader.ReadFile(filePath);
    Console.WriteLine($"Blob-Groesse: {blob.Length} Bytes");
    Console.WriteLine($"Dump ab 0x{offset:X} ({offset}), Laenge={length}");
    Console.WriteLine();
    BlobAnalyzer.HexDump(blob, offset, length);
}

void CmdStfsList(string packagePath)
{
    using var stfs = new StfsReader(packagePath);
    Console.WriteLine($"Magic:       {stfs.Magic}");
    Console.WriteLine($"ContentType: 0x{stfs.ContentType:X8}");
    Console.WriteLine($"TitleID:     0x{stfs.TitleId:X8}");
    Console.WriteLine($"DisplayName: {stfs.DisplayName}");
    Console.WriteLine($"Description: {stfs.Description}");
    Console.WriteLine();
    Console.WriteLine($"{"Name",-40} {"Groesse",12} {"StartBlock",10} {"Cons.",5}");
    Console.WriteLine(new string('-', 72));
    foreach (var f in stfs.Files)
    {
        var dirMark = f.IsDirectory ? "[DIR] " : "";
        Console.WriteLine($"{dirMark}{f.Name,-40} {f.Size,12:N0} {f.StartBlock,10} {(f.BlocksConsecutive ? "ja" : "nein"),5}");
    }
}

void CmdStfsExtract(string packagePath, string outputDir)
{
    using var stfs = new StfsReader(packagePath);
    Directory.CreateDirectory(outputDir);

    Console.WriteLine($"Extrahiere {stfs.Files.Count} Dateien nach {outputDir}...");
    foreach (var f in stfs.Files.Where(f => !f.IsDirectory))
    {
        var data = stfs.ExtractFile(f);
        var outPath = Path.Combine(outputDir, f.Name);
        File.WriteAllBytes(outPath, data);
        Console.WriteLine($"  {f.Name} ({data.Length:N0} Bytes)");
    }

    Console.WriteLine("Fertig.");
}

void CmdStfsRepack(string templatePath, string songDir, string outputPath)
{
    Console.WriteLine($"=== STFS Repack (Template-basiert) ===");
    Console.WriteLine($"Template: {templatePath}");
    Console.WriteLine($"Song-Ordner: {songDir}");
    Console.WriteLine();

    var templateBytes = File.ReadAllBytes(templatePath);
    Console.WriteLine($"Template-Groesse: {templateBytes.Length:N0} Bytes");

    var files = new Dictionary<string, byte[]>();
    foreach (var file in Directory.GetFiles(songDir))
    {
        var name = Path.GetFileName(file);
        files[name] = File.ReadAllBytes(file);
        Console.WriteLine($"  {name} ({files[name].Length:N0} Bytes)");
    }

    Console.WriteLine();
    Console.WriteLine("Verpacke mit Original-Header...");

    var result = StfsWriter.CreateFromTemplate(templateBytes, files);
    File.WriteAllBytes(outputPath, result);
    Console.WriteLine($"Geschrieben: {outputPath} ({result.Length:N0} Bytes)");

    // Verifizieren
    using var reader = new StfsReader(outputPath);
    Console.WriteLine($"Verifikation: {reader.Files.Count} Dateien, Magic={reader.Magic}");
    Console.WriteLine();
    Console.WriteLine("Auf die Xbox kopieren nach:");
    Console.WriteLine("  Content/0000000000000000/4D530888/00000002/");
}

void CmdCreateTestDlc(string songDir, string outputPath)
{
    Console.WriteLine($"=== DLC erstellen aus {songDir} ===");
    Console.WriteLine();

    // Alle Song-Dateien laden
    var files = new Dictionary<string, byte[]>();
    var songName = "";

    foreach (var file in Directory.GetFiles(songDir))
    {
        var name = Path.GetFileName(file);
        files[name] = File.ReadAllBytes(file);

        if (name.EndsWith(".X360") && !name.Contains("_Lyric") && !name.StartsWith("GES"))
        {
            songName = Path.GetFileNameWithoutExtension(name);
        }
    }

    if (string.IsNullOrEmpty(songName))
    {
        Console.WriteLine("FEHLER: Keine .X360 Chart-Datei im Ordner gefunden.");
        return;
    }

    Console.WriteLine($"Song: {songName}");
    Console.WriteLine($"Dateien: {files.Count}");
    foreach (var f in files)
        Console.WriteLine($"  {f.Key} ({f.Value.Length:N0} Bytes)");

    // Chart lesen um Metadaten zu extrahieren
    var chartPath = Path.Combine(songDir, $"{songName}.X360");
    var (header, blob) = X360Reader.ReadFile(chartPath);
    var deser = new IxbDeserializer(blob, header);
    var chart = deser.FindAndReadObject("lpsChart");

    var title = chart?["m_strName"]?.ToString() ?? songName;
    Console.WriteLine($"Titel: {title}");

    // DLC.xml erzeugen mit eindeutiger ID
    Console.WriteLine("Erzeuge DLC.xml...");
    var dlcInput = new LipsSongPackageBuilder.SongInput
    {
        Title = title,
        Artist = "Unknown Artist",
        Genre = "Pop",
        Year = "2024",
        Language = "EN",
        LengthSeconds = 200,
    };
    files["DLC.xml"] = LipsSongPackageBuilder.BuildDlcXml(dlcInput, songName);

    // STFS LIVE-Paket erstellen
    Console.WriteLine("Erstelle STFS LIVE-Paket...");
    var stfsData = StfsWriter.CreatePackage(
        files,
        $"\"{title}\"",
        $"Custom DLC: {title}");

    File.WriteAllBytes(outputPath, stfsData);
    Console.WriteLine($"DLC geschrieben: {outputPath} ({stfsData.Length:N0} Bytes)");
    Console.WriteLine();

    // Verifizieren
    using var reader = new StfsReader(outputPath);
    Console.WriteLine($"Verifikation: {reader.Files.Count} Dateien gelesen");
    foreach (var f in reader.Files)
        Console.WriteLine($"  {f.Name} ({f.Size:N0} Bytes)");

    Console.WriteLine();
    Console.WriteLine("=== Anleitung ===");
    Console.WriteLine("1. Datei auf USB-Stick oder Xbox-HDD kopieren nach:");
    Console.WriteLine("   Content/0000000000000000/4D530888/00000002/");
    Console.WriteLine("2. Dateiname kann beliebig sein (kein .bin/.live Extension noetig)");
    Console.WriteLine("3. Lips starten - Song sollte im DLC-Bereich erscheinen");
}

void CmdDumpDb(string dbPath)
{
    using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();

    // Tabellen
    cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' ORDER BY name";
    Console.WriteLine("=== TABELLEN ===");
    using (var reader = cmd.ExecuteReader())
    {
        while (reader.Read())
        {
            Console.WriteLine($"\n{reader.GetString(0)}:");
            Console.WriteLine($"  {reader.GetString(1)}");
        }
    }

    // Zeilen pro Tabelle
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
    var tables = new List<string>();
    using (var reader = cmd.ExecuteReader()) { while (reader.Read()) tables.Add(reader.GetString(0)); }

    foreach (var table in tables)
    {
        cmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
        var count = (long)cmd.ExecuteScalar()!;
        Console.WriteLine($"\n=== {table} ({count} Zeilen, erste 5) ===");

        cmd.CommandText = $"SELECT * FROM [{table}] LIMIT 5";
        using var reader = cmd.ExecuteReader();
        var cols = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++) cols.Add(reader.GetName(i));
        Console.WriteLine(string.Join(" | ", cols));
        Console.WriteLine(new string('-', Math.Min(120, cols.Count * 20)));
        while (reader.Read())
        {
            var vals = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
                vals.Add(reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "");
            Console.WriteLine(string.Join(" | ", vals));
        }
    }
}

void CmdConvertUltraStar(string txtPath, string outputDir)
{
    Console.WriteLine($"=== UltraStar -> Lips konvertieren ===");
    Console.WriteLine($"Quelle: {txtPath}");
    Console.WriteLine();

    var content = File.ReadAllText(txtPath);
    var song = UltraStarParser.Parse(content);

    Console.WriteLine($"Titel:    {song.Title}");
    Console.WriteLine($"Artist:   {song.Artist}");
    Console.WriteLine($"BPM:      {song.RealBpm:F0} (UltraStar: {song.Bpm})");
    Console.WriteLine($"GAP:      {song.GapMs}ms");
    Console.WriteLine($"Noten:    {song.SingableNotes.Count}");
    Console.WriteLine($"Dauer:    {song.DurationSeconds:F1}s");
    Console.WriteLine();

    Directory.CreateDirectory(outputDir);

    // Song-Paket erstellen
    var input = new LipsSongPackageBuilder.SongInput
    {
        Title = song.Title,
        Artist = song.Artist,
        Genre = song.Genre.Length > 0 ? song.Genre : "Pop",
        Year = song.Year.Length > 0 ? song.Year : "2024",
        Language = song.Language.Length > 0 ? song.Language : "EN",
        LengthSeconds = (int)song.DurationSeconds,
        UltraStarSong = song,
    };

    var pkg = LipsSongPackageBuilder.Build(input);

    // Einzeldateien speichern
    Console.WriteLine("Generierte Dateien:");
    foreach (var (name, data) in pkg.Files)
    {
        var outPath = Path.Combine(outputDir, name);
        File.WriteAllBytes(outPath, data);
        Console.WriteLine($"  {name} ({data.Length:N0} Bytes)");
    }

    // STFS-Paket erstellen
    var stfsPath = Path.Combine(outputDir, $"{song.Title}_LipsDLC");
    var stfsData = StfsWriter.CreatePackage(
        pkg.Files,
        $"\"{song.Title}\"",
        $"{song.Artist} - {song.Title}",
        titleId: 0x4D530888,
        contentType: 0x00000002);
    File.WriteAllBytes(stfsPath, stfsData);
    Console.WriteLine($"  {Path.GetFileName(stfsPath)} ({stfsData.Length:N0} Bytes) [STFS LIVE-Paket]");

    Console.WriteLine();
    Console.WriteLine("=== HINWEIS ===");
    Console.WriteLine("Das Paket enthaelt KEIN Audio. Du musst noch hinzufuegen:");
    Console.WriteLine($"  {song.Title}.xWMA     - Haupt-Audio (konvertieren mit xWMAEncode)");
    Console.WriteLine($"  {song.Title}_prv.xWMA - Audio-Preview (15-30s Ausschnitt)");
    Console.WriteLine($"  {song.Title}.jpg      - Album-Cover");
    Console.WriteLine();
    Console.WriteLine("Sobald Audio+Cover vorhanden, nochmal create-test-dlc ausfuehren:");
    Console.WriteLine($"  dotnet run -- create-test-dlc \"{outputDir}\" \"{stfsPath}\"");
}

void CmdDump(string filePath, string className)
{
    var (header, blob) = X360Reader.ReadFile(filePath);
    var deser = new IxbDeserializer(blob, header);

    var obj = deser.FindAndReadObject(className);
    if (obj == null)
    {
        Console.WriteLine($"Klasse '{className}' nicht gefunden oder kein Objekt im Blob.");
        Console.WriteLine("Gefundene Objekte:");
        foreach (var o in deser.Objects)
            Console.WriteLine($"  {o.ClassName} (size={o.Size})");
        return;
    }

    Console.WriteLine($"=== {className} ===");
    Console.WriteLine();
    PrintObject(obj, 0);
}

void CmdSongInfo(string filePath)
{
    var (header, blob) = X360Reader.ReadFile(filePath);
    var deser = new IxbDeserializer(blob, header);

    Console.WriteLine($"=== Song Info: {filePath} ===");
    Console.WriteLine();

    // Versuche alle relevanten Song-Klassen
    foreach (var className in new[]
                 { "lpsChart", "lpsMusicIndex", "lpsMusicInfo", "ls2MusicData" })
    {
        var obj = deser.FindAndReadObject(className);
        if (obj == null) continue;

        Console.WriteLine($"--- {className} ---");
        PrintSongFields(obj, 0);
        Console.WriteLine();
    }
}

void CmdChart(string filePath)
{
    var (header, blob) = X360Reader.ReadFile(filePath);
    var deser = new IxbDeserializer(blob, header);

    Console.WriteLine($"=== Chart Analyse: {filePath} ===");
    Console.WriteLine();

    // Alle Objekte ausgeben
    var allObjects = deser.ReadAllObjects();
    foreach (var obj in allObjects)
    {
        var cls = obj.TryGetValue("__class", out var cn) ? cn?.ToString() : "?";
        Console.WriteLine($"--- {cls} ---");
        PrintObject(obj, 0);
        Console.WriteLine();
    }
}

void CmdExportJson(string filePath, string? outputPath)
{
    var (header, blob) = X360Reader.ReadFile(filePath);
    var deser = new IxbDeserializer(blob, header);

    var allObjects = deser.ReadAllObjects();

    var jsonObj = allObjects.Select(ConvertToJsonCompatible).ToList();
    var json = JsonSerializer.Serialize(jsonObj, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });

    if (outputPath != null)
    {
        File.WriteAllText(outputPath, json);
        Console.WriteLine($"JSON exportiert nach: {outputPath}");
    }
    else
    {
        Console.WriteLine(json);
    }
}

// ──────────────────────────────────────────────────────────────
// Hilfsfunktionen
// ──────────────────────────────────────────────────────────────

void PrintSongFields(Dictionary<string, object?> obj, int indent)
{
    var songFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Title", "Artist", "Album", "Genre", "Year", "Rating", "Length",
        "Color", "Language", "AudioUri", "LyricUri", "VideoUri",
        "AlbumJacketUri", "UintID", "PreviewLyric", "PreviewAudioUri",
        "PreviewVideoUri", "PreviewIconUri", "ChartUri",
        "m_Title", "m_Artist", "m_Album", "m_Genre", "m_Year",
        "m_Rating", "m_Length", "m_Type", "m_strName",
        "m_strNoiseMaker", "m_MusicStartOffset", "m_BaseCentOffset"
    };

    foreach (var kv in obj)
    {
        if (kv.Key.StartsWith("__")) continue;

        if (kv.Value is Dictionary<string, object?> sub)
        {
            PrintSongFields(sub, indent);
        }
        else if (songFields.Contains(kv.Key))
        {
            var prefix = new string(' ', indent * 2);
            Console.WriteLine($"{prefix}{kv.Key,-30} = {FormatValue(kv.Value)}");
        }
    }
}

void PrintObject(Dictionary<string, object?> obj, int indent)
{
    var prefix = new string(' ', indent * 2);

    foreach (var kv in obj)
    {
        if (kv.Key.StartsWith("__")) continue;

        switch (kv.Value)
        {
            case Dictionary<string, object?> subObj:
            {
                var subClass = subObj.TryGetValue("__class", out var sc)
                    ? sc?.ToString()
                    : "?";
                Console.WriteLine($"{prefix}{kv.Key}: [{subClass}]");
                PrintObject(subObj, indent + 1);
                break;
            }
            case VectorInfo vec:
            {
                Console.WriteLine(
                    $"{prefix}{kv.Key}: Vector<{vec.ElementType}> Count={vec.Count}");
                for (var i = 0; i < vec.Elements.Count; i++)
                {
                    var elem = vec.Elements[i];
                    switch (elem)
                    {
                        case Dictionary<string, object?> elemObj:
                        {
                            var ec = elemObj.TryGetValue("__class", out var ecn)
                                ? ecn?.ToString()
                                : "?";
                            Console.WriteLine($"{prefix}  [{i}] {ec}:");
                            PrintObject(elemObj, indent + 2);
                            break;
                        }
                        case null:
                            Console.WriteLine($"{prefix}  [{i}] null");
                            break;
                        default:
                            Console.WriteLine($"{prefix}  [{i}] {FormatValue(elem)}");
                            break;
                    }
                }

                break;
            }
            default:
                Console.WriteLine($"{prefix}{kv.Key,-40} = {FormatValue(kv.Value)}");
                break;
        }
    }
}

string FormatValue(object? value)
{
    return value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        float f => $"{f:F4}f",
        double d => $"{d:F6}",
        uint u => $"{u} (0x{u:X8})",
        int i => $"{i}",
        bool b => b ? "true" : "false",
        byte b => $"{b} (0x{b:X2})",
        ushort us => $"{us} (0x{us:X4})",
        long l => $"{l} (0x{l:X16})",
        ulong ul => $"{ul} (0x{ul:X16})",
        byte[] raw => $"[{raw.Length} bytes] {BitConverter.ToString(raw, 0, Math.Min(raw.Length, 16))}",
        _ => value.ToString() ?? "?"
    };
}

object? ConvertToJsonCompatible(object? value)
{
    return value switch
    {
        null => null,
        Dictionary<string, object?> dict => dict.ToDictionary(
            kv => kv.Key,
            kv => ConvertToJsonCompatible(kv.Value)),
        VectorInfo vec => new Dictionary<string, object?>
        {
            ["__type"] = $"Vector<{vec.ElementType}>",
            ["count"] = vec.Count,
            ["elements"] = vec.Elements.Select(ConvertToJsonCompatible).ToList()
        },
        byte[] raw => BitConverter.ToString(raw),
        float f => f,
        double d => d,
        _ => value
    };
}
