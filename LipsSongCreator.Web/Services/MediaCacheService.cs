using System.Collections.Concurrent;

namespace LipsSongCreator.Web.Services;

/// <summary>
/// Singleton-Cache fuer Medien-Dateien (Audio/Video), damit der Browser
/// sie ueber einen HTTP-Endpoint streamen kann (Audio-Vorschau im Editor).
/// SignalR/Blazor-Server kann grosse Dateien nicht effizient pushen.
/// </summary>
public class MediaCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>Legt Medien ab und gibt den Abruf-Schluessel zurueck.</summary>
    public string Store(byte[] data, string fileName)
    {
        var key = Guid.NewGuid().ToString("N");
        _cache[key] = new CacheEntry(data, GetContentType(fileName), DateTime.UtcNow);
        Cleanup();
        return key;
    }

    public CacheEntry? Get(string key) => _cache.TryGetValue(key, out var e) ? e : null;

    public void Remove(string key) => _cache.TryRemove(key, out _);

    /// <summary>Entfernt Eintraege aelter als 2 Stunden.</summary>
    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddHours(-2);
        foreach (var kv in _cache.Where(kv => kv.Value.Created < cutoff).ToList())
            _cache.TryRemove(kv.Key, out _);
    }

    private static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream"
        };

    public record CacheEntry(byte[] Data, string ContentType, DateTime Created);
}
