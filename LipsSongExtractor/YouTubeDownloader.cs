using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LipsSongExtractor;

/// <summary>
/// Laedt Video + Audio + Thumbnail von YouTube via yt-dlp herunter.
/// yt-dlp wird im tools/-Ordner oder PATH gesucht (wie ffmpeg).
///
/// Bot-Erkennung: YouTube blockt anonyme Downloads von Server-IPs
/// ("Sign in to confirm you're not a bot"). Loesung: Cookies mitgeben.
///   1. cookies.txt im tools/-Ordner (Netscape-Format, exportiert mit
///      einer Browser-Extension wie "Get cookies.txt LOCALLY")
///   2. Umgebungsvariable YTDLP_COOKIES (Pfad zur cookies.txt)
///   3. Umgebungsvariable YTDLP_COOKIES_BROWSER (z.B. "firefox" oder
///      "chrome") - nutzt --cookies-from-browser, funktioniert nur wenn
///      auf derselben Maschine ein Browser mit YouTube-Session laeuft
/// </summary>
public static class YouTubeDownloader
{
    public static string? FindYtDlp() => ToolLocator.Find("yt-dlp", "YTDLP_PATH");

    public static bool IsAvailable => FindYtDlp() != null;

    /// <summary>
    /// Sucht eine cookies.txt fuer die YouTube-Authentifizierung.
    /// </summary>
    public static string? FindCookiesFile()
    {
        // 1. Umgebungsvariable
        var envPath = Environment.GetEnvironmentVariable("YTDLP_COOKIES");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // 2. cookies.txt im tools/-Ordner (gleiche Suche wie die Tools selbst)
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = start;
            for (var i = 0; i < 6 && dir != null; i++)
            {
                var p = Path.Combine(dir, "tools", "cookies.txt");
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
        }
        return null;
    }

    /// <summary>
    /// Baut das Cookie-Argument fuer yt-dlp (oder leer).
    /// </summary>
    private static string BuildCookieArg()
    {
        var cookiesFile = FindCookiesFile();
        if (cookiesFile != null)
            return $"--cookies \"{cookiesFile}\"";

        var browser = Environment.GetEnvironmentVariable("YTDLP_COOKIES_BROWSER");
        if (!string.IsNullOrEmpty(browser))
            return $"--cookies-from-browser {browser}";

        return "";
    }

    /// <summary>True wenn eine Cookie-Quelle konfiguriert ist.</summary>
    public static bool HasCookieSource =>
        FindCookiesFile() != null ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("YTDLP_COOKIES_BROWSER"));

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
                "yt-dlp nicht gefunden. Bitte yt-dlp in den tools/-Ordner legen.\n" +
                "Download: https://github.com/yt-dlp/yt-dlp/releases");

        var ffmpeg = AudioConverter.FindFfmpeg();
        Directory.CreateDirectory(outputDir);

        var result = new DownloadResult();
        var cookieArg = BuildCookieArg();
        var ffmpegArg = ffmpeg != null ? $"--ffmpeg-location \"{Path.GetDirectoryName(ffmpeg)}\"" : "";

        // Video + Audio in bestmoeglicher Qualitaet (max 1080p)
        progress?.Invoke("Lade Video von YouTube...");
        RunYtDlp(ytDlp,
            $"-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]\" " +
            $"--merge-output-format mp4 " +
            $"{cookieArg} {ffmpegArg} " +
            $"-o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" " +
            $"--no-playlist --no-overwrites \"{url}\"");

        // Thumbnail
        progress?.Invoke("Lade Thumbnail...");
        RunYtDlp(ytDlp,
            $"--write-thumbnail --skip-download --convert-thumbnails jpg " +
            $"{cookieArg} {ffmpegArg} " +
            $"-o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" " +
            $"--no-playlist --no-overwrites \"{url}\"");

        // Titel extrahieren (fuer die UI)
        var titleOutput = RunYtDlpOutput(ytDlp, $"--get-title --no-playlist {cookieArg} \"{url}\"");
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
        var psi = ToolLocator.BuildStartInfo(exePath, arguments);
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Konnte yt-dlp nicht starten.");
        proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            var tail = stderr.Length > 500 ? stderr[^500..] : stderr;

            // Bot-Erkennung: klare Handlungsanleitung statt roher yt-dlp-Meldung
            if (tail.Contains("Sign in to confirm"))
            {
                throw new InvalidOperationException(
                    "YouTube blockiert den anonymen Download (Bot-Erkennung).\n\n" +
                    "Loesung: YouTube-Cookies bereitstellen:\n" +
                    "1. Browser-Extension 'Get cookies.txt LOCALLY' installieren\n" +
                    "2. Auf youtube.com einloggen und Cookies exportieren\n" +
                    "3. Die Datei als 'cookies.txt' in den tools/-Ordner legen\n\n" +
                    "Alternativ: Umgebungsvariable YTDLP_COOKIES=/pfad/zu/cookies.txt\n" +
                    "oder YTDLP_COOKIES_BROWSER=firefox (Browser auf derselben Maschine).");
            }

            throw new InvalidOperationException($"yt-dlp fehlgeschlagen:\n{tail}");
        }
    }

    private static string RunYtDlpOutput(string exePath, string arguments)
    {
        var psi = ToolLocator.BuildStartInfo(exePath, arguments);
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.StandardError.ReadToEnd();
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
