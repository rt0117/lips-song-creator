#if WINDOWS_TRANSCODER
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
#endif

namespace LipsSongExtractor;

/// <summary>
/// Konvertiert Videos ins Lips-kompatible WMV-Format.
///
/// KRITISCH: Der Lips-Videoplayer dekodiert nur WMV3 (VC-1) + WMA.
/// Verifiziert per Isolationstest: WMV2-Dateien werden von Lips ignoriert
/// (kein Preview-Video, Song-Start haengt), Original-WMV3 laeuft.
///
/// Windows: WinRT MediaTranscoder mit dem eingebauten WMV3-Encoder
///          (wmvencod.dll) - beste Qualitaet, exakt das Original-Format.
/// Linux:   ffmpeg mit vc1_qsv/vc1 ist NICHT verfuegbar (ffmpeg kann
///          VC-1 nur dekodieren). Fallback: ffmpeg-WMV2 mit Warnung -
///          es gibt derzeit KEINEN freien WMV3-Encoder fuer Linux.
///          Empfehlung fuer Linux-User: Video auf einem Windows-Rechner
///          konvertieren oder das DLC ohne Video bauen.
///
/// Ziel-Formate (aus Original-DLC "Happy Ending"):
///   Hauptvideo: WMV3 768x432, 29.97fps, ~3000 kbps + WMA 48kHz stereo
///   Preview:    WMV3 240x136, 29.97fps,  ~500 kbps + WMA 48kHz stereo
/// </summary>
public static class VideoConverter
{
    /// <summary>
    /// True wenn echtes WMV3-Encoding verfuegbar ist (nur Windows).
    /// </summary>
    public static bool IsWmv3Available =>
#if WINDOWS_TRANSCODER
        OperatingSystem.IsWindows();
#else
        false;
#endif

    /// <summary>
    /// Konvertiert ein Video (MP4 etc.) ins Lips-Hauptvideo-Format.
    /// </summary>
    public static void ConvertToMainWmv(string inputPath, string outputPath)
    {
#if WINDOWS_TRANSCODER
        TranscodeAsync(inputPath, outputPath, 768, 432, 3_000_000).GetAwaiter().GetResult();
#else
        ConvertWithFfmpegFallback(inputPath, outputPath, 768, 432, "3000k", null, null);
#endif
    }

    /// <summary>
    /// Erzeugt das Preview-Video (Ausschnitt ab startSeconds, 240x136).
    /// WICHTIG: gleicher Startpunkt wie die Audio-Preview, damit Video,
    /// Audio und PreviewLyric zusammenpassen.
    /// </summary>
    public static void CreatePreviewWmv(string inputPath, string outputPath,
        double startSeconds, int durationSeconds = AudioConverter.DefaultPreviewSeconds)
    {
#if WINDOWS_TRANSCODER
        TranscodeAsync(inputPath, outputPath, 240, 136, 500_000,
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(durationSeconds)).GetAwaiter().GetResult();
#else
        ConvertWithFfmpegFallback(inputPath, outputPath, 240, 136, "500k",
            startSeconds, durationSeconds);
#endif
    }

#if WINDOWS_TRANSCODER
    private static async Task TranscodeAsync(string inputPath, string outputPath,
        uint width, uint height, uint videoBitrate,
        TimeSpan? trimStart = null, TimeSpan? trimDuration = null)
    {
        var inputFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputPath));

        // Zieldatei anlegen (MediaTranscoder braucht eine existierende StorageFile)
        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDir);
        File.WriteAllBytes(outputPath, []);
        var outputFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(outputPath));

        // WMV-Profil. Default-Subtype ist WVC1 (VC-1 Advanced) - Original-DLCs
        // nutzen WMV3 (VC-1 Simple/Main), daher Subtype explizit setzen.
        var profile = MediaEncodingProfile.CreateWmv(VideoEncodingQuality.Wvga);
        profile.Video!.Subtype = "WMV3";
        profile.Video.Width = width;
        profile.Video.Height = height;
        profile.Video.Bitrate = videoBitrate;
        profile.Video.FrameRate!.Numerator = 30000;
        profile.Video.FrameRate.Denominator = 1001; // 29.97 fps wie Original
        profile.Audio!.SampleRate = 48000;
        profile.Audio.ChannelCount = 2;
        profile.Audio.Bitrate = 192_000;

        var transcoder = new MediaTranscoder();
        if (trimStart != null) transcoder.TrimStartTime = trimStart.Value;
        if (trimDuration != null) transcoder.TrimStopTime = trimStart!.Value + trimDuration.Value;

        var prep = await transcoder.PrepareFileTranscodeAsync(inputFile, outputFile, profile);
        if (!prep.CanTranscode)
            throw new InvalidOperationException(
                $"MediaTranscoder kann nicht transkodieren: {prep.FailureReason}");

        await prep.TranscodeAsync();
        VerifyAsfMagic(outputPath);
    }
#else
    /// <summary>
    /// Linux-Fallback: ffmpeg-WMV2. ACHTUNG: Lips spielt WMV2 nach unseren
    /// Tests NICHT ab - dieser Pfad existiert, damit die Pipeline unter
    /// Linux durchlaeuft. Fuer funktionierende Videos Windows verwenden.
    /// </summary>
    private static void ConvertWithFfmpegFallback(string inputPath, string outputPath,
        int width, int height, string bitrate, double? startSeconds, int? durationSeconds)
    {
        var ffmpeg = AudioConverter.FindFfmpeg()
            ?? throw new InvalidOperationException("ffmpeg nicht gefunden.");

        var trim = "";
        if (startSeconds != null)
        {
            var s = startSeconds.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            trim = $"-ss {s} -t {durationSeconds ?? 15} ";
        }

        var psi = ToolLocator.BuildStartInfo(ffmpeg,
            $"-y {trim}-i \"{inputPath}\" " +
            $"-vf \"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2\" " +
            $"-r 30000/1001 -c:v wmv2 -b:v {bitrate} " +
            $"-c:a wmav2 -b:a 192k -ar 48000 -ac 2 \"{outputPath}\"");

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.StandardError.ReadToEnd();
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg-Videokonvertierung fehlgeschlagen.");

        VerifyAsfMagic(outputPath);
    }
#endif

    private static void VerifyAsfMagic(string outputPath)
    {
        using var fs = File.OpenRead(outputPath);
        var magic = new byte[4];
        fs.ReadExactly(magic, 0, 4);
        if (magic[0] != 0x30 || magic[1] != 0x26 || magic[2] != 0xB2 || magic[3] != 0x75)
            throw new InvalidOperationException("Ausgabe ist kein gueltiges WMV (ASF-Magic fehlt).");
    }
}
