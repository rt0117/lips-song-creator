using System.Text;
using LipsSongExtractor.Poco;

namespace LipsSongExtractor;

/// <summary>
/// Deserialisiert IXB Binary Blobs korrekt.
///
/// IXB Format:
/// Der Blob enthält ZWEI Bereiche:
/// 1. Inline-Daten (Strings, Arrays) als [runtime_ptr:4][size:4][data:size bytes]
/// 2. Objekte mit fixen Runtime-Offsets, deren ixVector-Felder
///    _data-Pointer enthalten, die auf die Inline-Daten verweisen
///
/// Die Pointer sind ALTE Runtime-Adressen aus der Serialisierung.
/// Wir bauen eine Lookup-Tabelle: runtime_ptr -> (blob_offset, size, data)
/// um die Daten aufzulösen.
/// </summary>
public class IxbDeserializer
{
    private readonly byte[] _blob;
    private readonly Ixb _header;
    private readonly Dictionary<string, ClassDef> _classLookup;

    // Mapping: runtime_ptr -> InlineEntry
    private readonly Dictionary<uint, InlineEntry> _ptrLookup = new();

    // Alle gefundenen Objekt-Einträge (am Ende des Blobs)
    private readonly List<ObjectEntry> _objects = [];

    public IxbDeserializer(byte[] blob, Ixb header)
    {
        _blob = blob;
        _header = header;
        _classLookup = header.Classes.ClassList.ToDictionary(c => c.Name);

        // Strikter Parser zuerst (echtes IXB-Format mit Klassen-Index),
        // Heuristik nur als Fallback fuer beschaedigte Dateien.
        if (!TryStrictParse())
        {
            BuildPointerLookup();
            FindObjects();
        }
    }

    /// <summary>
    /// Strikter sequentieller Parser fuer das verifizierte IXB-Format:
    ///   Jeder Eintrag: [classIndex:4][runtime_ptr:4][size:4][data:size]
    ///   classIndex = 0: Inline-Daten (Strings/Arrays)
    ///   classIndex > 0: 1-basierter Index in die Classes-Liste
    /// Verifiziert gegen Original-DLCs (Happy Ending: 5483/5483 Eintraege).
    /// </summary>
    private bool TryStrictParse()
    {
        var classes = _header.Classes.ClassList;
        var pos = 0;
        var entries = new List<(int ClsIdx, uint Ptr, int Size, int DataOff)>();

        while (pos + 12 <= _blob.Length)
        {
            var clsIdx = (int)BlobAnalyzer.RU32(_blob, pos);
            var ptr = BlobAnalyzer.RU32(_blob, pos + 4);
            var size = BlobAnalyzer.RU32(_blob, pos + 8);

            if (clsIdx < 0 || clsIdx > classes.Count) return false;
            if (ptr == 0 || size == 0) return false;
            if (pos + 12 + (long)size > _blob.Length) return false;

            // Objekt-Eintraege muessen zur Klassengroesse passen
            if (clsIdx > 0 && classes[clsIdx - 1].Size != (int)size) return false;

            entries.Add((clsIdx, ptr, (int)size, pos + 12));
            pos += 12 + (int)size;
        }

        // Der Blob muss vollstaendig konsumiert sein (bis auf < 12 Padding-Bytes)
        if (_blob.Length - pos >= 12) return false;
        if (entries.Count == 0) return false;

        foreach (var (clsIdx, ptr, size, dataOff) in entries)
        {
            if (!_ptrLookup.ContainsKey(ptr))
            {
                _ptrLookup[ptr] = new InlineEntry
                {
                    RuntimePtr = ptr,
                    BlobOffset = dataOff,
                    Size = size
                };
            }

            if (clsIdx > 0)
            {
                var cls = classes[clsIdx - 1];
                _objects.Add(new ObjectEntry
                {
                    RuntimePtr = ptr,
                    BlobOffset = dataOff,
                    Size = size,
                    ClassName = cls.Name,
                    ClassDef = cls
                });
            }
        }

        return true;
    }

    /// <summary>
    /// Phase 1 (Fallback): Scanne den Blob nach [ptr:4][size:4][data:size] Einträgen
    /// und baue die Pointer-Lookup-Tabelle auf.
    /// </summary>
    private void BuildPointerLookup()
    {
        // Alle bekannten Klassen-Größen
        var classSizes = new HashSet<int>(_header.Classes.ClassList.Select(c => c.Size));

        // Scanne sequentiell vom Anfang
        var pos = 0;

        // Das erste uint32 kann 0x00000000 sein (vtable placeholder) - überspringen
        if (_blob.Length >= 4 && BlobAnalyzer.RU32(_blob, 0) == 0)
            pos = 4;

        while (pos + 8 <= _blob.Length)
        {
            var ptr = BlobAnalyzer.RU32(_blob, pos);
            var size = BlobAnalyzer.RU32(_blob, pos + 4);

            // Null-Werte überspringen
            if (ptr == 0)
            {
                pos += 4;
                continue;
            }

            // Gültigkeitscheck: Size muss vernünftig sein und Daten müssen im Blob liegen
            if (size == 0 || size > (uint)(_blob.Length - pos - 8))
            {
                pos += 4;
                continue;
            }

            var dataOffset = pos + 8;
            var dataEnd = dataOffset + (int)size;

            // Wenn der Pointer schon als Objekt bekannt ist, überspringen
            // (Objekte werden separat geparst)
            if (classSizes.Contains((int)size) && dataEnd <= _blob.Length)
            {
                // Könnte ein Objekt ODER Inline-Daten sein
                // Wir speichern beides in der Lookup-Tabelle
            }

            if (!_ptrLookup.ContainsKey(ptr))
            {
                _ptrLookup[ptr] = new InlineEntry
                {
                    RuntimePtr = ptr,
                    BlobOffset = dataOffset,
                    Size = (int)size
                };
            }

            pos = dataEnd;
        }
    }

    /// <summary>
    /// Phase 2: Identifiziere Objekte in der Pointer-Lookup-Tabelle.
    /// Jeder Eintrag dessen Size einer bekannten Klassengröße entspricht,
    /// ist potentiell ein Objekt. Zusätzlich wird vom Ende rückwärts geparst.
    /// </summary>
    private void FindObjects()
    {
        var classBySize = new Dictionary<int, List<ClassDef>>();
        foreach (var cls in _header.Classes.ClassList)
        {
            if (!classBySize.ContainsKey(cls.Size))
                classBySize[cls.Size] = [];
            classBySize[cls.Size].Add(cls);
        }

        // Strategie 1: Alle Pointer-Lookup-Einträge prüfen, deren Size
        // einer Klassengröße entspricht
        foreach (var kvp in _ptrLookup.OrderBy(k => k.Value.BlobOffset))
        {
            var entry = kvp.Value;
            if (!classBySize.TryGetValue(entry.Size, out var candidates)) continue;

            // Wähle die beste Klasse (bevorzuge abgeleitete Klassen)
            var cls = candidates.OrderByDescending(c => c.Size).First();

            _objects.Add(new ObjectEntry
            {
                RuntimePtr = entry.RuntimePtr,
                BlobOffset = entry.BlobOffset,
                Size = entry.Size,
                ClassName = cls.Name,
                ClassDef = cls
            });
        }

        // Strategie 2: Rückwärts vom Ende nach weiteren Objekten suchen
        var usedOffsets = new HashSet<int>(_objects.Select(o => o.BlobOffset));
        var pos = _blob.Length;

        while (pos >= 8)
        {
            var found = false;

            foreach (var kvp in classBySize.OrderByDescending(k => k.Key))
            {
                var trySize = kvp.Key;
                var tryStart = pos - trySize - 8;
                if (tryStart < 0) continue;

                var tryPtr = BlobAnalyzer.RU32(_blob, tryStart);
                var tryLen = (int)BlobAnalyzer.RU32(_blob, tryStart + 4);
                var dataOff = tryStart + 8;

                if (tryLen == trySize && tryPtr != 0 && !usedOffsets.Contains(dataOff))
                {
                    var cls = kvp.Value.OrderByDescending(c => c.Size).First();

                    _objects.Add(new ObjectEntry
                    {
                        RuntimePtr = tryPtr,
                        BlobOffset = dataOff,
                        Size = trySize,
                        ClassName = cls.Name,
                        ClassDef = cls
                    });
                    usedOffsets.Add(dataOff);

                    if (!_ptrLookup.ContainsKey(tryPtr))
                    {
                        _ptrLookup[tryPtr] = new InlineEntry
                        {
                            RuntimePtr = tryPtr,
                            BlobOffset = dataOff,
                            Size = trySize
                        };
                    }

                    pos = tryStart;
                    found = true;
                    break;
                }
            }

            if (!found) break;
        }

        // Sortiere Objekte nach Blob-Offset
        _objects.Sort((a, b) => a.BlobOffset.CompareTo(b.BlobOffset));
    }

    /// <summary>
    /// Liest ein Objekt einer bestimmten Klasse aus dem Blob.
    /// Verwendet Runtime-Offsets und löst Pointer über die Lookup-Tabelle auf.
    /// </summary>
    public Dictionary<string, object?> ReadObject(ClassDef cls, int blobOffset,
        int depth = 0)
    {
        var dict = new Dictionary<string, object?>();
        dict["__class"] = cls.Name;
        dict["__offset"] = $"0x{blobOffset:X}";

        var members = cls.AllMembers.OrderBy(m => m.Offset).ToList();

        for (var i = 0; i < members.Count; i++)
        {
            var mem = members[i];
            var fieldStart = blobOffset + mem.Offset;

            // Berechne Feldgröße aus dem Abstand zum nächsten Member
            var nextOffset = i + 1 < members.Count ? members[i + 1].Offset : cls.Size;
            var fieldSize = nextOffset - mem.Offset;

            if (fieldStart + 4 > _blob.Length) break;

            // Hat das Member ein explizites Type-Attribut?
            var hasExplicitType = !string.IsNullOrWhiteSpace(mem.Type);

            // --- 16-Byte-Felder: ixVector<char> (String) oder ixVector<T> (Container) ---
            if (fieldSize == 16)
            {
                if (IsStringField(mem))
                {
                    dict[mem.Name] = ReadInlineString(fieldStart);
                    continue;
                }

                if (IsFixedDataField(mem))
                {
                    // Feste Datenfelder (Hashes, etc.) als Roh-Bytes lesen
                    var raw = new byte[fieldSize];
                    Array.Copy(_blob, fieldStart, raw, 0, fieldSize);
                    dict[mem.Name] = raw;
                    continue;
                }

                // Jedes andere 16-Byte-Feld versuchen wir als Container
                // zu interpretieren (ixVector/ixList mit Pointer-Elementen)
                dict[mem.Name] = ReadContainer(mem, fieldStart, depth);
                continue;
            }

            // --- 12-Byte-Felder: ixList ---
            if (fieldSize == 12)
            {
                // ixList hat _root(4), _size(4), _allocator(4)
                // Wir lesen es als Container
                dict[mem.Name] = ReadContainer(mem, fieldStart, depth);
                continue;
            }

            // --- 4-Byte-Felder: Pointer oder Primitiv ---
            if (fieldSize == 4 && IsPointerField(mem))
            {
                dict[mem.Name] = ReadPointerField(mem, fieldStart, depth);
                continue;
            }

            // --- Inline-Struct (z.B. Tone = 8 Bytes mit fIdx + octave) ---
            if (fieldSize > 4 && fieldSize <= 64 && depth < 8 && !IsKnownPrimitiveField(mem))
            {
                var inlineCls = FindInlineStructClass(mem, fieldSize);
                if (inlineCls != null)
                {
                    dict[mem.Name] = ReadObject(inlineCls, fieldStart, depth + 1);
                    continue;
                }
            }

            // --- Primitives Feld ---
            if (fieldStart + fieldSize <= _blob.Length)
            {
                var raw = new byte[fieldSize];
                Array.Copy(_blob, fieldStart, raw, 0, fieldSize);
                dict[mem.Name] = FieldSizes.ConvertByType(mem.Type, mem.Name, raw, true);
            }
        }

        return dict;
    }

    /// <summary>
    /// Liest einen String aus einem ixVector<char>-Feld.
    /// </summary>
    private string ReadInlineString(int fieldStart)
    {
        var dataPtr = BlobAnalyzer.RU32(_blob, fieldStart);
        var reserve = BlobAnalyzer.RU32(_blob, fieldStart + 4);
        var size = (int)BlobAnalyzer.RU32(_blob, fieldStart + 8);

        if (dataPtr == 0 || size == 0) return "";

        // Suche Daten über Pointer-Lookup
        if (_ptrLookup.TryGetValue(dataPtr, out var entry))
        {
            var strLen = Math.Min(size, entry.Size);
            if (entry.BlobOffset + strLen <= _blob.Length)
            {
                return Encoding.UTF8.GetString(_blob, entry.BlobOffset, strLen).TrimEnd('\0');
            }
        }

        return $"<unresolved:0x{dataPtr:X8} size={size}>";
    }

    /// <summary>
    /// Liest einen Container (ixVector/ixList) mit typisierten Elementen.
    /// </summary>
    private object ReadContainer(MemberDef mem, int fieldStart, int depth)
    {
        var dataPtr = BlobAnalyzer.RU32(_blob, fieldStart);
        var reserve = BlobAnalyzer.RU32(_blob, fieldStart + 4);
        var count = (int)BlobAnalyzer.RU32(_blob, fieldStart + 8);

        var elementType = ExtractTemplateArg(mem.Type ?? "");
        var isPointer = elementType.EndsWith("*") || string.IsNullOrEmpty(elementType);
        var elementBase = elementType.TrimEnd('*').Trim();

        // Wenn kein expliziter Typ, versuche ihn aus dem Feldnamen abzuleiten
        if (string.IsNullOrEmpty(elementBase))
            elementBase = InferElementTypeFromFieldName(mem.Name);

        var result = new VectorInfo
        {
            ElementType = string.IsNullOrEmpty(elementBase) ? "<auto>" : elementBase + (isPointer ? "*" : ""),
            Count = count,
            Reserve = (int)reserve
        };

        if (dataPtr == 0 || count == 0)
            return result;

        // Finde die Array-Daten über Pointer-Lookup
        if (!_ptrLookup.TryGetValue(dataPtr, out var entry))
            return result;

        // Wenn wir den Elementtyp kennen und eine Klasse dafür haben
        if (!string.IsNullOrEmpty(elementBase) &&
            _classLookup.TryGetValue(elementBase, out var elemCls) && depth < 8)
        {
            if (isPointer)
                ReadPointerElements(result, entry, count, elemCls, depth);
            else
                ReadInlineElements(result, entry, count, elemCls, depth);
        }
        // Kein expliziter Typ -> versuche als Pointer-Array mit Auto-Erkennung
        else if (depth < 8)
        {
            ReadAutoPointerElements(result, entry, count, depth);
        }

        return result;
    }

    /// <summary>
    /// Leitet den Element-Typ aus dem Feldnamen ab.
    /// z.B. "m_vpSeqCode" -> Basis "ixSeqCode", "m_vpSequence" -> "ixSequence"
    /// </summary>
    private string InferElementTypeFromFieldName(string fieldName)
    {
        // Bekannte Vektor-Feldnamen und ihre Elementtypen
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "m_vpSeqCode", "ixSeqCode" },
            { "m_vpSequence", "ixSequence" },
            { "m_vpExtraSequence", "ixSequence" },
            { "m_vpListeners", "ixSeqCode" },
            { "m_vecpLedSequence", "lpsLedSequence" },
            { "m_vecpExtraLedSequence", "lpsLedSequence" },
            { "m_vpAssets", "ixAsset" },
        };

        if (mappings.TryGetValue(fieldName, out var mapped))
            return mapped;

        return "";
    }

    /// <summary>
    /// Liest ein Array von Pointern auf Objekte eines bekannten Typs.
    /// </summary>
    private void ReadPointerElements(VectorInfo result, InlineEntry entry,
        int count, ClassDef elemCls, int depth)
    {
        for (var i = 0; i < count; i++)
        {
            var ptrOff = entry.BlobOffset + i * 4;
            if (ptrOff + 4 > _blob.Length) break;

            var elemPtr = BlobAnalyzer.RU32(_blob, ptrOff);
            if (elemPtr == 0)
            {
                result.Elements.Add(null);
                continue;
            }

            if (_ptrLookup.TryGetValue(elemPtr, out var elemEntry))
            {
                var actualCls = DetermineActualClass(elemEntry, elemCls);
                var elem = ReadObject(actualCls, elemEntry.BlobOffset, depth + 1);
                result.Elements.Add(elem);
            }
            else
            {
                result.Elements.Add($"<ptr:0x{elemPtr:X8}>");
            }
        }
    }

    /// <summary>
    /// Liest ein inline Array von Structs.
    /// </summary>
    private void ReadInlineElements(VectorInfo result, InlineEntry entry,
        int count, ClassDef elemCls, int depth)
    {
        for (var i = 0; i < count; i++)
        {
            var elemOff = entry.BlobOffset + i * elemCls.Size;
            if (elemOff + elemCls.Size > _blob.Length) break;

            var elem = ReadObject(elemCls, elemOff, depth + 1);
            result.Elements.Add(elem);
        }
    }

    /// <summary>
    /// Liest ein Array von Pointern ohne bekannten Elementtyp.
    /// Bestimmt die Klasse jedes Elements anhand der Größe im Pointer-Lookup.
    /// </summary>
    private void ReadAutoPointerElements(VectorInfo result, InlineEntry entry,
        int count, int depth)
    {
        for (var i = 0; i < count; i++)
        {
            var ptrOff = entry.BlobOffset + i * 4;
            if (ptrOff + 4 > _blob.Length) break;

            var elemPtr = BlobAnalyzer.RU32(_blob, ptrOff);
            if (elemPtr == 0)
            {
                result.Elements.Add(null);
                continue;
            }

            if (_ptrLookup.TryGetValue(elemPtr, out var elemEntry))
            {
                // Finde die Klasse anhand der Größe
                var cls = FindClassBySize(elemEntry.Size);
                if (cls != null)
                {
                    var elem = ReadObject(cls, elemEntry.BlobOffset, depth + 1);
                    result.Elements.Add(elem);
                }
                else
                {
                    result.Elements.Add($"<ptr:0x{elemPtr:X8} size={elemEntry.Size}>");
                }
            }
            else
            {
                result.Elements.Add($"<ptr:0x{elemPtr:X8}>");
            }
        }
    }

    private ClassDef? FindClassBySize(int size)
    {
        // Bevorzuge abgeleitete Klassen (größere Vererbungstiefe)
        return _header.Classes.ClassList
            .Where(c => c.Size == size)
            .OrderByDescending(InheritanceDepth)
            .FirstOrDefault();
    }

    private static int InheritanceDepth(ClassDef cls)
    {
        var depth = 0;
        var current = cls;
        while (current.Parent != null) { depth++; current = current.Parent; }
        return depth;
    }

    /// <summary>
    /// Bestimmt die tatsächliche Klasse eines Objekts basierend auf der Größe.
    /// Bei Vererbung kann ein Pointer auf eine abgeleitete Klasse zeigen.
    /// </summary>
    private ClassDef DetermineActualClass(InlineEntry entry, ClassDef baseClass)
    {
        // Suche eine Klasse deren Size zum Entry passt
        foreach (var cls in _header.Classes.ClassList)
        {
            if (cls.Size == entry.Size && IsSubclassOf(cls, baseClass))
                return cls;
        }

        return baseClass;
    }

    private bool IsSubclassOf(ClassDef cls, ClassDef baseClass)
    {
        if (cls.Name == baseClass.Name) return true;
        var current = cls;
        while (current.Parent != null)
        {
            if (current.Parent.Name == baseClass.Name) return true;
            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// Liest ein Pointer-Feld und löst es optional zu einem Objekt auf.
    /// </summary>
    private object? ReadPointerField(MemberDef mem, int fieldStart, int depth)
    {
        var ptr = BlobAnalyzer.RU32(_blob, fieldStart);
        if (ptr == 0) return null;

        if (depth >= 8) return $"<ptr:0x{ptr:X8} max-depth>";

        // Versuche den Pointer aufzulösen
        if (_ptrLookup.TryGetValue(ptr, out var entry))
        {
            // Bestimme den Typ des Zielobjekts
            var targetTypeName = mem.Type?.TrimEnd('*').Trim();
            if (targetTypeName != null && _classLookup.TryGetValue(targetTypeName, out var targetCls))
            {
                // Bestimme tatsächliche Klasse
                var actualCls = DetermineActualClass(entry, targetCls);
                return ReadObject(actualCls, entry.BlobOffset, depth + 1);
            }

            // Kein Typ bekannt -> gib Pointer zurück
            return $"<ptr:0x{ptr:X8} -> offset=0x{entry.BlobOffset:X}>";
        }

        return $"<unresolved:0x{ptr:X8}>";
    }

    private bool IsStringField(MemberDef mem)
    {
        var name = mem.Name.ToLowerInvariant();
        // ixVector<char>-Felder: Strings, Pfade, URIs, etc.
        return name.Contains("str") || name.Contains("name") || name.Contains("uri") ||
               name.Contains("path") || name.Contains("word") || name.Contains("tag") ||
               name.Contains("typename") || name.Contains("filename") ||
               name.Contains("lyric") || name.Contains("freeword") ||
               name.Contains("contentid") || name.Contains("releasedate") ||
               name.Contains("latestdate") || name.Contains("contentfilename") ||
               name == "m_vdata" || // ixFileImage Dateiinhalt
               name.Contains("previewlyric") || name.Contains("audioeffect") ||
               name.Contains("noisem"); // NoiseMaker strings
    }

    private bool IsContainerField(MemberDef mem)
    {
        var name = mem.Name.ToLowerInvariant();
        return name.Contains("vec") || name.Contains("lst") || name.Contains("list") ||
               name.StartsWith("m_vp") || name.StartsWith("m_vec") ||
               name.StartsWith("_data");
    }

    private bool IsPointerField(MemberDef mem)
    {
        var name = mem.Name;
        return (name.StartsWith("m_p") && !name.StartsWith("m_pp")) ||
               (mem.Type != null && mem.Type.Trim().EndsWith("*"));
    }

    /// <summary>
    /// Erkennt 16-Byte-Felder die feste Daten enthalten (keine Container/Strings).
    /// z.B. Hashes, Farben, oder andere fixe Strukturen.
    /// </summary>
    private static bool IsFixedDataField(MemberDef mem)
    {
        var name = mem.Name.ToLowerInvariant();
        return name.Contains("hash") || name.Contains("color") ||
               name.Contains("xuid") || name.Contains("deviceid") ||
               name.Contains("usercolor");
    }

    /// <summary>
    /// Felder die NICHT als Inline-Struct interpretiert werden sollen,
    /// auch wenn ihre Größe einer Klasse entspricht.
    /// </summary>
    private static bool IsKnownPrimitiveField(MemberDef mem)
    {
        var name = mem.Name.ToLowerInvariant();
        return name.Contains("color") || name.Contains("usercolor") ||
               name.Contains("packed") || name.Contains("reserve") ||
               name.Contains("vowel") || name.Contains("brightness") ||
               name.Contains("calculated");
    }

    /// <summary>
    /// Findet eine Klasse die als Inline-Struct an einem Member-Offset eingebettet ist.
    /// z.B. Tone (8 Bytes) in lpsMelodyMarker.m_Tone
    /// </summary>
    private ClassDef? FindInlineStructClass(MemberDef mem, int fieldSize)
    {
        // Bekannte Inline-Structs über den Feldnamen zuordnen
        var nameMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "m_Tone", "Tone" },
            { "m_Tempo", "ixSeqTempoCode" }, // Nein, Tempo ist ein eigenes Feld, kein Struct
        };

        // Exakte Namens-Zuordnung
        if (nameMappings.TryGetValue(mem.Name, out var mappedName) &&
            _classLookup.TryGetValue(mappedName, out var mappedCls) &&
            mappedCls.Size == fieldSize)
        {
            return mappedCls;
        }

        // Suche eine Klasse deren Size passt UND die Member hat (= echtes Struct, nicht Basisklasse)
        // Nur für kleine Größen (8, 12) die typisch für Inline-Structs sind
        if (fieldSize <= 12)
        {
            var candidates = _header.Classes.ClassList
                .Where(c => c.Size == fieldSize && c.Members.Count > 0 && c.Parent == null)
                .ToList();

            if (candidates.Count == 1)
                return candidates[0];
        }

        return null;
    }

    private static string ExtractTemplateArg(string type)
    {
        var lt = type.IndexOf('<');
        var gt = type.LastIndexOf('>');
        if (lt < 0 || gt < 0) return "";
        var inner = type.Substring(lt + 1, gt - lt - 1);
        var comma = inner.IndexOf(',');
        if (comma > 0) inner = inner[..comma];
        return inner.Trim();
    }

    // --- Öffentliche API ---

    /// <summary>
    /// Gibt alle gefundenen Objekt-Einträge zurück.
    /// </summary>
    public IReadOnlyList<ObjectEntry> Objects => _objects;

    /// <summary>
    /// Gibt die Pointer-Lookup-Tabelle zurück.
    /// </summary>
    public IReadOnlyDictionary<uint, InlineEntry> PointerLookup => _ptrLookup;

    /// <summary>
    /// Findet ein Objekt einer bestimmten Klasse und liest es.
    /// </summary>
    public Dictionary<string, object?>? FindAndReadObject(string className)
    {
        var obj = _objects.FirstOrDefault(o => o.ClassName == className);
        if (obj == null) return null;

        if (!_classLookup.TryGetValue(className, out var cls)) return null;
        return ReadObject(cls, obj.BlobOffset);
    }

    /// <summary>
    /// Liest alle Objekte und gibt sie als Liste zurück.
    /// </summary>
    public List<Dictionary<string, object?>> ReadAllObjects()
    {
        var results = new List<Dictionary<string, object?>>();
        foreach (var obj in _objects)
        {
            if (obj.ClassDef != null)
            {
                results.Add(ReadObject(obj.ClassDef, obj.BlobOffset));
            }
        }

        return results;
    }

    /// <summary>
    /// Druckt eine Zusammenfassung der gefundenen Einträge.
    /// </summary>
    public void PrintSummary()
    {
        Console.WriteLine($"=== IXB Deserialisierung ===");
        Console.WriteLine($"Blob: {_blob.Length} Bytes, Header: {_header.NumOfElements} Elemente");
        Console.WriteLine($"Pointer-Lookup: {_ptrLookup.Count} Eintraege");
        Console.WriteLine($"Objekte: {_objects.Count} gefunden");
        Console.WriteLine();

        Console.WriteLine("--- Pointer-Lookup-Tabelle ---");
        foreach (var kvp in _ptrLookup.OrderBy(k => k.Value.BlobOffset))
        {
            var e = kvp.Value;
            var preview = "";
            var len = Math.Min(e.Size, 40);
            for (var i = 0; i < len && e.BlobOffset + i < _blob.Length; i++)
            {
                var b = _blob[e.BlobOffset + i];
                preview += b >= 32 && b <= 126 ? (char)b : '.';
            }

            Console.WriteLine(
                $"  0x{e.RuntimePtr:X8} -> off=0x{e.BlobOffset:X4} size={e.Size,6} '{preview}'");
        }

        Console.WriteLine();
        Console.WriteLine("--- Objekte ---");
        foreach (var obj in _objects)
        {
            Console.WriteLine(
                $"  0x{obj.RuntimePtr:X8} off=0x{obj.BlobOffset:X4} size={obj.Size,5} {obj.ClassName}");
        }
    }
}

public class InlineEntry
{
    public uint RuntimePtr { get; set; }
    public int BlobOffset { get; set; }
    public int Size { get; set; }
}

public class ObjectEntry
{
    public uint RuntimePtr { get; set; }
    public int BlobOffset { get; set; }
    public int Size { get; set; }
    public string ClassName { get; set; } = "";
    public ClassDef? ClassDef { get; set; }
}
