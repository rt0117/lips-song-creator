using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LipsSongExtractor;

/// <summary>
/// Laedt Video + Audio + Thumbnail von YouTube via yt-dlp herunter.
/// yt-dlp wird im tools/-Ordner oder PATH gesucht (wie ffmpeg).
/// </summary>
public static class YouTubeDownloader
{
    public static string? FindYtDlp() => ToolLocator.Find("yt-dlp", "YTDLP_PATH");

    public static bool IsAvailable => FindYtDlp() != null;

    /// <summary>
    /// Laedt Video + Thumbnail von YouTube.
    /// </summary>
    /// <param name="url">YouTube-URL</param>
    /// <param name="outputDir">Zielverzeichnis (wird erstellt)</param>
    /// <param name="progress">Fortschritts-Callback</param>
    /// <returns>Pfade der heruntergeladenen Dateien</returns>
    public static DownloadResult Download(string url, string outputDir, Action<string>? progress = null)
    {
        var ytDlp = FindYtDlp()
            ?? throw new InvalidOperationException(
                "yt-dlp nicht gefunden. Bitte yt-dlp.exe in den tools/-Ordner legen.\n" +
                "Download: https://github.com/yt-dlp/yt-dlp/releases");

        var ffmpeg = AudioConverter.FindFfmpeg();
        Directory.CreateDirectory(outputDir);

        var result = new DownloadResult();

        // Video + Audio in bestmoeglicher Qualitaet (max 1080p)
        progress?.Invoke("Lade Video von YouTube...");
        var ffmpegArg = ffmpeg != null ? $"--ffmpeg-location \"{Path.GetDirectoryName(ffmpeg)}\"" : "";
        RunYtDlp(ytDlp,
            $"-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]\" " +
            $"--merge-output-format mp4 " +
            $"{ffmpegArg} " +
            $"-o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" " +
            $"--no-playlist --no-overwrites \"{url}\"");

        // Thumbnail
        progress?.Invoke("Lade Thumbnail...");
        RunYtDlp(ytDlp,
            $"--write-thumbnail --skip-download --convert-thumbnails jpg " +
            $"{ffmpegArg} " +
            $"-o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" " +
            $"--no-playlist --no-overwrites \"{url}\"");

        // Titel extrahieren (fuer die UI)
        var titleOutput = RunYtDlpOutput(ytDlp, $"--get-title --no-playlist \"{url}\"");
        result.Title = titleOutput.Trim();

        // Dateien finden
        result.VideoPath = Directory.GetFiles(outputDir, "*.mp4").FirstOrDefault()
                        ?? Directory.GetFiles(outputDir, "*.webm").FirstOrDefault();
        result.ThumbnailPath = Directory.GetFiles(outputDir, "*.jpg").FirstOrDefault()
                            ?? Directory.GetFiles(outputDir, "*.webp").FirstOrDefault()
                            ?? Directory.GetFiles(outputDir, "*.png").FirstOrDefault();

        return result;
    }

    /// <summary>
    /// Extrahiert die Video-ID fuer die Validierung.
    /// </summary>
    public static bool IsValidUrl(string url)
    {
        return Regex.IsMatch(url,
            @"(youtube\.com/(watch\?v=|shorts/|embed/)|youtu\.be/)[a-zA-Z0-9_-]{11}");
    }

    private static void RunYtDlp(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Konnte yt-dlp nicht starten.");
        proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            var tail = stderr.Length > 500 ? stderr[^500..] : stderr;
            throw new InvalidOperationException($"yt-dlp fehlgeschlagen:\n{tail}");
        }
    }

    private static string RunYtDlpOutput(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }

    public class DownloadResult
    {
        public string Title { get; set; } = "";
        public string? VideoPath { get; set; }
        public string? ThumbnailPath { get; set; }
    }
}
