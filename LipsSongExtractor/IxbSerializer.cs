using System.Text;
using LipsSongExtractor.Poco;

namespace LipsSongExtractor;

/// <summary>
/// Serialisiert IXB-Objekte zurück in den Binary Blob.
///
/// Blob-Format:
///   Der Blob enthält eine Folge von Einträgen: [runtime_ptr:4][size:4][data:size bytes]
///   - Inline-Daten (Strings, Array-Daten) kommen zuerst
///   - Objekte (mit fixer Klassen-Größe) kommen danach
///   - Die runtime_ptr-Werte sind konsistente Adressen, die Pointer in Objekten
///     auf die entsprechenden Inline-Daten/Objekte verweisen
///
/// Strategie für Roundtrip:
///   Statt den Blob komplett neu zu bauen, modifizieren wir den bestehenden Blob:
///   1. Lese den Original-Blob mit dem Deserializer (Pointer-Lookup + Objekte)
///   2. Ändere die gewünschten Felder in den Roh-Bytes direkt
///   3. Schreibe den modifizierten Blob zurück
///
///   Für NEUE Songs (Phase 3+) bauen wir den Blob from scratch.
/// </summary>
public class IxbSerializer
{
    private readonly byte[] _blob;
    private readonly Ixb _header;
    private readonly IxbDeserializer _deserializer;

    public IxbSerializer(byte[] originalBlob, Ixb header)
    {
        _blob = (byte[])originalBlob.Clone(); // Arbeitskopie
        _header = header;
        _deserializer = new IxbDeserializer(originalBlob, header);
    }

    /// <summary>
    /// Gibt den aktuellen Zustand des Blobs zurück.
    /// </summary>
    public byte[] GetBlob() => (byte[])_blob.Clone();

    /// <summary>
    /// Schreibt einen Big-Endian uint32 an eine Position im Blob.
    /// </summary>
    public void WriteU32(int offset, uint value)
    {
        _blob[offset] = (byte)(value >> 24);
        _blob[offset + 1] = (byte)(value >> 16);
        _blob[offset + 2] = (byte)(value >> 8);
        _blob[offset + 3] = (byte)value;
    }

    /// <summary>
    /// Schreibt einen Big-Endian int32 an eine Position im Blob.
    /// </summary>
    public void WriteI32(int offset, int value) => WriteU32(offset, (uint)value);

    /// <summary>
    /// Schreibt einen Big-Endian float an eine Position im Blob.
    /// </summary>
    public void WriteF32(int offset, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        Array.Copy(bytes, 0, _blob, offset, 4);
    }

    /// <summary>
    /// Ändert ein primitives Feld eines Objekts im Blob.
    /// </summary>
    /// <param name="className">Name der Klasse (z.B. "lpsChart")</param>
    /// <param name="fieldName">Name des Feldes (z.B. "m_BaseCentOffset")</param>
    /// <param name="value">Neuer Wert (uint, int, float)</param>
    public bool SetField(string className, string fieldName, object value)
    {
        var obj = _deserializer.Objects.FirstOrDefault(o => o.ClassName == className);
        if (obj == null) return false;

        var cls = _header.Classes.ClassList.FirstOrDefault(c => c.Name == className);
        if (cls == null) return false;

        var mem = cls.AllMembers.FirstOrDefault(m => m.Name == fieldName);
        if (mem == null) return false;

        var fieldOffset = obj.BlobOffset + mem.Offset;

        switch (value)
        {
            case uint u:
                WriteU32(fieldOffset, u);
                return true;
            case int i:
                WriteI32(fieldOffset, i);
                return true;
            case float f:
                WriteF32(fieldOffset, f);
                return true;
            case bool b:
                WriteU32(fieldOffset, b ? 1u : 0u);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Ändert den String-Inhalt eines ixVector&lt;char&gt;-Feldes.
    /// ACHTUNG: Der neue String muss kürzer oder gleich lang sein wie der alte,
    /// da wir den Blob nicht vergrößern (das würde alle Offsets verschieben).
    /// </summary>
    public bool SetString(string className, string fieldName, string newValue)
    {
        var obj = _deserializer.Objects.FirstOrDefault(o => o.ClassName == className);
        if (obj == null) return false;

        var cls = _header.Classes.ClassList.FirstOrDefault(c => c.Name == className);
        if (cls == null) return false;

        var mem = cls.AllMembers.FirstOrDefault(m => m.Name == fieldName);
        if (mem == null) return false;

        var fieldOffset = obj.BlobOffset + mem.Offset;

        // Lese den aktuellen _data-Pointer und _size
        var dataPtr = BlobAnalyzer.RU32(_blob, fieldOffset);
        var reserve = (int)BlobAnalyzer.RU32(_blob, fieldOffset + 4);
        var currentSize = (int)BlobAnalyzer.RU32(_blob, fieldOffset + 8);

        if (dataPtr == 0) return false;

        // Finde den Inline-Eintrag über den Pointer
        if (!_deserializer.PointerLookup.TryGetValue(dataPtr, out var entry))
            return false;

        var newBytes = Encoding.UTF8.GetBytes(newValue + "\0");

        // Prüfe ob der neue String in den bestehenden Platz passt
        if (newBytes.Length > entry.Size)
            return false; // Zu lang - würde den Blob vergrößern

        // Schreibe den neuen String in den Inline-Datenbereich
        // Nullen zum Auffüllen, damit der restliche Platz sauber ist
        Array.Clear(_blob, entry.BlobOffset, entry.Size);
        Array.Copy(newBytes, 0, _blob, entry.BlobOffset, newBytes.Length);

        // Aktualisiere _size im Vektor-Feld (Anzahl Bytes inkl. Null-Terminator)
        WriteU32(fieldOffset + 8, (uint)newBytes.Length);

        // WICHTIG: Die Entry-Size (bei entry.BlobOffset - 4) wird NICHT geändert,
        // damit die Blob-Struktur intakt bleibt. Der übrige Platz wird mit Nullen gefüllt.

        return true;
    }
}
