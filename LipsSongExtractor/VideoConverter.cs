using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace LipsSongExtractor;

/// <summary>
/// Konvertiert Videos ins Lips-kompatible WMV-Format via Windows
/// MediaTranscoder (WinRT).
///
/// KRITISCH: Der Lips-Videoplayer dekodiert nur WMV3 (VC-1) + WMA.
/// ffmpeg kann kein WMV3 encodieren (nur decodieren) - der in Windows
/// eingebaute Media-Foundation-Encoder (wmvencod.dll) aber schon.
/// Verifiziert per Isolationstest: WMV2-Dateien werden von Lips ignoriert
/// (kein Preview-Video, Song-Start haengt), Original-WMV3 laeuft.
///
/// Ziel-Formate (aus Original-DLC "Happy Ending"):
///   Hauptvideo: WMV3 768x432, 29.97fps, ~3000 kbps + WMA 48kHz stereo
///   Preview:    WMV3 240x136, 29.97fps,  ~500 kbps + WMA 48kHz stereo
/// </summary>
public static class VideoConverter
{
    /// <summary>
    /// Konvertiert ein Video (MP4 etc.) ins Lips-Hauptvideo-Format.
    /// Hinweis: Das Seitenverhaeltnis wird auf 16:9 skaliert (Lips-Standard).
    /// </summary>
    public static void ConvertToMainWmv(string inputPath, string outputPath)
    {
        TranscodeAsync(inputPath, outputPath, 768, 432, 3_000_000).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Erzeugt das Preview-Video (Ausschnitt ab startSeconds, 240x136).
    /// WICHTIG: gleicher Startpunkt wie die Audio-Preview, damit Video,
    /// Audio und PreviewLyric zusammenpassen.
    /// </summary>
    public static void CreatePreviewWmv(string inputPath, string outputPath,
        double startSeconds, int durationSeconds = AudioConverter.DefaultPreviewSeconds)
    {
        TranscodeAsync(inputPath, outputPath, 240, 136, 500_000,
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(durationSeconds)).GetAwaiter().GetResult();
    }

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

        // Sanity-Check: Ausgabe muss ein ASF-Container sein (WMV-Magic)
        using var fs = File.OpenRead(outputPath);
        var magic = new byte[4];
        fs.ReadExactly(magic, 0, 4);
        if (magic[0] != 0x30 || magic[1] != 0x26 || magic[2] != 0xB2 || magic[3] != 0x75)
            throw new InvalidOperationException("Ausgabe ist kein gueltiges WMV (ASF-Magic fehlt).");
    }
}
