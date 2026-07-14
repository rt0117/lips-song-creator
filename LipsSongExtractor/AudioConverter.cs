using System.Diagnostics;

namespace LipsSongExtractor;

/// <summary>
/// Konvertiert Audio-Dateien (MP3, OGG, WAV, ...) ins Xbox xWMA-Format
/// mithilfe externer Tools:
///
///   ffmpeg.exe      - dekodiert beliebige Formate zu WAV (48 kHz, Stereo, 16-bit)
///                     https://www.gyan.dev/ffmpeg/builds/ (release essentials)
///   xWMAEncode.exe  - encodiert WAV zu xWMA (aus dem DirectX SDK Juni 2010)
///
/// Ziel-Format (verifiziert gegen Original-Lips-DLCs):
///   RIFF/XWMA, WMAv2 (0x0161), 48000 Hz, Stereo, 192 kbps (AvgBytesPerSec=24000)
///
/// Tool-Suche (in dieser Reihenfolge):
///   1. Umgebungsvariablen FFMPEG_PATH / XWMAENCODE_PATH (voller Pfad zur .exe)
///   2. "tools"-Ordner im Arbeitsverzeichnis bzw. neben der Anwendung
///   3. PATH
/// </summary>
public static class AudioConverter
{
    /// <summary>Ziel-Bitrate in bit/s (Original-DLCs: 192 kbps).</summary>
    public const int TargetBitrate = 192000;

    /// <summary>Standard-Laenge der Song-Preview in Sekunden.</summary>
    public const int DefaultPreviewSeconds = 15;

    public static string? FindFfmpeg() => FindTool("ffmpeg", "FFMPEG_PATH");

    public static string? FindXwmaEncode() => FindTool("xWMAEncode", "XWMAENCODE_PATH");

    /// <summary>
    /// Prueft ob beide Tools verfuegbar sind. Gibt eine Fehlermeldung mit
    /// Download-Hinweisen zurueck, wenn etwas fehlt (sonst null).
    /// </summary>
    public static string? CheckTools()
    {
        var missing = new List<string>();
        if (FindFfmpeg() == null)
            missing.Add("  ffmpeg.exe      Download: https://www.gyan.dev/ffmpeg/builds/ (release essentials)");
        if (FindXwmaEncode() == null)
            missing.Add("  xWMAEncode.exe  Aus dem DirectX SDK (Juni 2010), Utilities\\bin\\x86");

        if (missing.Count == 0) return null;

        return "Fehlende Tools fuer die Audio-Konvertierung:\n" +
               string.Join("\n", missing) + "\n\n" +
               "Loesung: Die .exe-Dateien in einen 'tools'-Ordner im Projektverzeichnis legen,\n" +
               "in den PATH aufnehmen, oder Umgebungsvariablen FFMPEG_PATH/XWMAENCODE_PATH setzen.";
    }

    /// <summary>
    /// Konvertiert eine beliebige Audio-Datei zu xWMA (48 kHz, Stereo, 192 kbps).
    /// </summary>
    public static void ConvertToXwma(string inputPath, string outputPath)
    {
        var tempWav = Path.Combine(Path.GetTempPath(), $"lips_audio_{Guid.NewGuid()}.wav");
        try
        {
            // 1. ffmpeg: Input -> WAV 48kHz Stereo 16-bit
            RunTool(RequireFfmpeg(),
                $"-y -i \"{inputPath}\" -ar 48000 -ac 2 -c:a pcm_s16le \"{tempWav}\"");

            // 2. xWMAEncode: WAV -> xWMA
            EncodeWavToXwma(tempWav, outputPath);
        }
        finally
        {
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
    }

    /// <summary>
    /// Erzeugt eine Preview-xWMA (Ausschnitt mit Fade-In/Out).
    /// </summary>
    public static void CreatePreviewXwma(string inputPath, string outputPath,
        double startSeconds, int durationSeconds = DefaultPreviewSeconds)
    {
        var tempWav = Path.Combine(Path.GetTempPath(), $"lips_prv_{Guid.NewGuid()}.wav");
        try
        {
            var fadeOutStart = Math.Max(0, durationSeconds - 2);
            var start = startSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var fadeOut = fadeOutStart.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            RunTool(RequireFfmpeg(),
                $"-y -ss {start} -t {durationSeconds} -i \"{inputPath}\" " +
                $"-af \"afade=t=in:d=0.5,afade=t=out:st={fadeOut}:d=2\" " +
                $"-ar 48000 -ac 2 -c:a pcm_s16le \"{tempWav}\"");

            EncodeWavToXwma(tempWav, outputPath);
        }
        finally
        {
            if (File.Exists(tempWav)) File.Delete(tempWav);
        }
    }

    /// <summary>
    /// Konvertiert ein Cover-Bild zu JPEG (max. 512x512, wie Original-DLC-Cover).
    /// </summary>
    public static void ConvertCoverToJpg(string inputPath, string outputPath)
    {
        RunTool(RequireFfmpeg(),
            $"-y -i \"{inputPath}\" -vf \"scale='min(512,iw)':'min(512,ih)':force_original_aspect_ratio=decrease\" " +
            $"-q:v 2 \"{outputPath}\"");
    }

    // HINWEIS: Video-Konvertierung ist in VideoConverter.cs (WinRT
    // MediaTranscoder) - ffmpeg kann kein WMV3/VC-1 encodieren und
    // WMV2-Dateien werden von Lips nicht abgespielt!

    /// <summary>
    /// Prueft ob eine Datei eine Videospur enthaelt.
    /// </summary>
    public static bool HasVideoStream(string inputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = RequireFfmpeg(),
            Arguments = $"-i \"{inputPath}\" -hide_banner",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return stderr.Contains("Video:") && !stderr.Contains("Video: mjpeg") &&
               !stderr.Contains("Video: png");
    }

    /// <summary>
    /// Ermittelt die Laenge einer Audio-Datei in Sekunden (via ffmpeg).
    /// </summary>
    public static double GetDurationSeconds(string inputPath)
    {
        // ffmpeg schreibt die Duration nach stderr
        var psi = new ProcessStartInfo
        {
            FileName = RequireFfmpeg(),
            Arguments = $"-i \"{inputPath}\" -f null -",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        var match = System.Text.RegularExpressions.Regex.Match(
            stderr, @"Duration:\s*(\d+):(\d+):(\d+\.?\d*)");
        if (!match.Success) return 0;

        return int.Parse(match.Groups[1].Value) * 3600
             + int.Parse(match.Groups[2].Value) * 60
             + double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void EncodeWavToXwma(string wavPath, string xwmaPath)
    {
        RunTool(RequireXwmaEncode(), $"-b {TargetBitrate} \"{wavPath}\" \"{xwmaPath}\"");

        if (!File.Exists(xwmaPath))
            throw new InvalidOperationException($"xWMAEncode hat keine Ausgabedatei erzeugt: {xwmaPath}");

        // Sanity-Check: RIFF/XWMA Magic
        using var fs = File.OpenRead(xwmaPath);
        var magic = new byte[12];
        fs.ReadExactly(magic, 0, 12);
        if (magic[0] != 'R' || magic[1] != 'I' || magic[2] != 'F' || magic[3] != 'F' ||
            magic[8] != 'X' || magic[9] != 'W' || magic[10] != 'M' || magic[11] != 'A')
            throw new InvalidOperationException("Ausgabe ist kein gueltiges xWMA (RIFF/XWMA Magic fehlt).");
    }

    private static string RequireFfmpeg() =>
        FindFfmpeg() ?? throw new InvalidOperationException(
            "ffmpeg.exe nicht gefunden. " + CheckTools());

    private static string RequireXwmaEncode() =>
        FindXwmaEncode() ?? throw new InvalidOperationException(
            "xWMAEncode.exe nicht gefunden. " + CheckTools());

    private static void RunTool(string exePath, string arguments)
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
            ?? throw new InvalidOperationException($"Konnte {exePath} nicht starten.");

        var stderr = proc.StandardError.ReadToEnd();
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            var tail = stderr.Length > 800 ? stderr[^800..] : stderr;
            throw new InvalidOperationException(
                $"{Path.GetFileName(exePath)} fehlgeschlagen (ExitCode {proc.ExitCode}):\n{tail}");
        }
    }

    private static string? FindTool(string baseName, string envVar)
    {
        // 1. Umgebungsvariable
        var envPath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        var exeName = OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

        // 2. tools-Ordner (Arbeitsverzeichnis + neben der Anwendung)
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "tools", exeName),
            Path.Combine(AppContext.BaseDirectory, "tools", exeName),
            Path.Combine(Environment.CurrentDirectory, exeName),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // 3. PATH
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
        {
            var p = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(p)) return p;
        }

        return null;
    }
}
