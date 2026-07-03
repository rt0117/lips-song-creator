using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Schreibt eine .X360-Datei aus den Roh-Bestandteilen.
/// 
/// Dateiformat:
///   [XML-Header inkl. Klassen-Definitionen]
///   <Objects>[Binary Blob]</Objects>
///   </ixb>
/// </summary>
public static class X360Writer
{
    /// <summary>
    /// Schreibt eine .X360-Datei aus XML-Header-Bytes und Binary Blob.
    /// </summary>
    public static void WriteFile(string path, byte[] headerBytes, byte[] binaryBlob)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(headerBytes);
        fs.Write(Encoding.ASCII.GetBytes("<Objects>"));
        fs.Write(binaryBlob);
        fs.Write(Encoding.ASCII.GetBytes("</Objects></ixb>"));
    }

    /// <summary>
    /// Liest eine .X360-Datei und gibt die drei Roh-Bestandteile zurück:
    /// 1. headerBytes: Alles VOR &lt;Objects&gt; (XML-Header)
    /// 2. binaryBlob: Alles ZWISCHEN &lt;Objects&gt; und &lt;/Objects&gt;
    /// 3. trailerBytes: Alles NACH &lt;/Objects&gt; (normalerweise &lt;/ixb&gt;)
    /// </summary>
    public static (byte[] headerBytes, byte[] binaryBlob, byte[] trailerBytes) ReadRawParts(string path)
    {
        var allBytes = File.ReadAllBytes(path);

        var openTag = Encoding.ASCII.GetBytes("<Objects>");
        var closeTag = Encoding.ASCII.GetBytes("</Objects>");

        var objStart = IndexOf(allBytes, openTag);
        var objEnd = IndexOf(allBytes, closeTag);

        if (objStart < 0 || objEnd < 0 || objEnd <= objStart)
            throw new InvalidDataException("Kann <Objects>...</Objects> nicht finden.");

        // Header: alles vor <Objects>
        var headerBytes = new byte[objStart];
        Buffer.BlockCopy(allBytes, 0, headerBytes, 0, objStart);

        // Blob: zwischen <Objects> und </Objects>
        var blobStart = objStart + openTag.Length;
        var blobLen = objEnd - blobStart;
        var binaryBlob = new byte[blobLen];
        Buffer.BlockCopy(allBytes, blobStart, binaryBlob, 0, blobLen);

        // Trailer: ab </Objects> bis Ende
        var trailerStart = objEnd;
        var trailerLen = allBytes.Length - trailerStart;
        var trailerBytes = new byte[trailerLen];
        Buffer.BlockCopy(allBytes, trailerStart, trailerBytes, 0, trailerLen);

        return (headerBytes, binaryBlob, trailerBytes);
    }

    /// <summary>
    /// Schreibt eine .X360-Datei exakt aus den drei Roh-Bestandteilen.
    /// Header + &lt;Objects&gt; + Blob + Trailer (enthält &lt;/Objects&gt;&lt;/ixb&gt;)
    /// </summary>
    public static void WriteFileRaw(string path, byte[] headerBytes, byte[] binaryBlob,
        byte[] trailerBytes)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(headerBytes);
        fs.Write(Encoding.ASCII.GetBytes("<Objects>"));
        fs.Write(binaryBlob);
        fs.Write(trailerBytes);
    }

    private static int IndexOf(byte[] source, byte[] pattern)
    {
        if (pattern.Length == 0) return 0;
        for (var i = 0; i <= source.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j]) { match = false; break; }
            }

            if (match) return i;
        }

        return -1;
    }
}
