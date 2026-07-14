using System.Text;

namespace LipsSongExtractor;

/// <summary>
/// Komplett-Pipeline: UltraStar-Song (txt + Audio/Video + Cover) -> fertiges
/// Lips STFS-DLC. Wird von CLI (Program.cs) und Web-UI gemeinsam genutzt.
/// </summary>
public static class UltraStarDlcPipeline
{
    public class Result
    {
        /// <summary>Voller Pfad des fertigen STFS-Pakets.</summary>
        public string StfsPath { get; set; } = "";

        /// <summary>Dateiname des Pakets (= ContentID + "4D").</summary>
        public string StfsFileName { get; set; } = "";

        public long StfsSize { get; set; }
        public bool HasAudio { get; set; }
        public bool HasVideo { get; set; }
        public List<string> Warnings { get; } = [];
    }

    /// <summary>
    /// Fuehrt die komplette Konvertierung aus.
    /// </summary>
    /// <param name="txtPath">Pfad zur UltraStar .txt</param>
    /// <param name="outputDir">Ausgabe-Verzeichnis (wird erstellt)</param>
    /// <param name="progress">Fortschritts-Callback (Statustext)</param>
    public static Result Run(string txtPath, string outputDir, Action<string>? progress = null)
    {
        var result = new Result();
        void Report(string msg) => progress?.Invoke(msg);

        var songDir = Path.GetDirectoryName(Path.GetFullPath(txtPath)) ?? ".";
        var song = UltraStarParser.Parse(File.ReadAllText(txtPath));
        Directory.CreateDirectory(outputDir);

        // ── 1. Quell-Dateien finden ─────────────────────────────────
        Report("Suche Audio/Video/Cover...");
        var toolError = AudioConverter.CheckTools();
        if (toolError != null) result.Warnings.Add(toolError);

        var audioPath = FindMediaFile(songDir, song.AudioFile,
            [".mp3", ".m4a", ".ogg", ".flac", ".wav", ".wma", ".mp4", ".mkv", ".webm"],
            preferAudio: true);

        var videoPath = FindMediaFile(songDir, song.VideoFile,
            [".mp4", ".mkv", ".webm", ".avi", ".wmv", ".mov"], preferAudio: false);
        var hasVideo = videoPath != null && toolError == null &&
                       AudioConverter.HasVideoStream(videoPath);

        // Audio-Dauer: Song laeuft bis zum Audio-Ende (ausklingen)
        if (audioPath != null && toolError == null)
            song.AudioDurationSeconds = AudioConverter.GetDurationSeconds(audioPath);

        var previewStart = (double)song.PreviewStartSeconds;
        if (previewStart <= 0 && song.AudioDurationSeconds > 0)
            previewStart = song.AudioDurationSeconds * 0.3;

        // ── 2. Chart + Lyric + DLC.xml ──────────────────────────────
        Report("Generiere Chart und Lyrics...");
        var input = new LipsSongPackageBuilder.SongInput
        {
            Title = song.Title,
            Artist = song.Artist,
            Genre = song.Genre.Length > 0 ? song.Genre : "Pop",
            Year = song.Year.Length > 0 ? song.Year : "2024",
            Language = song.Language.Length > 0 ? song.Language : "EN",
            LengthSeconds = (int)song.EffectiveEndSeconds,
            UltraStarSong = song,
            PreviewStartSeconds = previewStart,
            HasVideo = hasVideo,
        };
        var pkg = LipsSongPackageBuilder.Build(input);

        // ── 3. Audio konvertieren ───────────────────────────────────
        if (audioPath != null && toolError == null)
        {
            Report("Konvertiere Audio (xWMA)...");
            var xwmaPath = Path.Combine(outputDir, $"{song.Title}.xWMA");
            AudioConverter.ConvertToXwma(audioPath, xwmaPath);
            pkg.Files[$"{song.Title}.xWMA"] = File.ReadAllBytes(xwmaPath);

            Report("Erzeuge Audio-Preview...");
            var prvPath = Path.Combine(outputDir, $"{song.Title}_prv.xWMA");
            AudioConverter.CreatePreviewXwma(audioPath, prvPath, previewStart);
            pkg.Files[$"{song.Title}_prv.xWMA"] = File.ReadAllBytes(prvPath);
            result.HasAudio = true;
        }
        else
        {
            result.Warnings.Add($"Kein Audio konvertiert ({song.AudioFile}).");
        }

        // ── 4. Video konvertieren (WMV3 via Windows MediaTranscoder) ──
        if (hasVideo && !VideoConverter.IsWmv3Available)
        {
            result.Warnings.Add(
                "Video-Encoding auf dieser Plattform erzeugt WMV2 statt WMV3 - " +
                "Lips spielt WMV2 NICHT ab. Fuer funktionierende Videos das DLC " +
                "unter Windows bauen (oder ohne Video exportieren).");
        }
        if (hasVideo)
        {
            Report("Konvertiere Musikvideo (WMV3, kann einige Minuten dauern)...");
            var wmvPath = Path.Combine(outputDir, $"{song.Title}.wmv");
            VideoConverter.ConvertToMainWmv(videoPath!, wmvPath);
            pkg.Files[$"{song.Title}.wmv"] = File.ReadAllBytes(wmvPath);

            Report("Erzeuge Video-Preview...");
            var prvWmvPath = Path.Combine(outputDir, $"{song.Title}_prv.wmv");
            VideoConverter.CreatePreviewWmv(videoPath!, prvWmvPath, previewStart);
            pkg.Files[$"{song.Title}_prv.wmv"] = File.ReadAllBytes(prvWmvPath);
            result.HasVideo = true;
        }

        // ── 5. Cover ─────────────────────────────────────────────────
        var coverPath = song.CoverFile.Length > 0 ? Path.Combine(songDir, song.CoverFile) : null;
        if (coverPath == null || !File.Exists(coverPath))
        {
            coverPath = Directory.GetFiles(songDir)
                .FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant()
                    is ".jpg" or ".jpeg" or ".png");
        }

        if (coverPath != null && File.Exists(coverPath))
        {
            var jpgName = $"{song.Title}.jpg";
            if (toolError == null)
            {
                Report("Konvertiere Cover...");
                var jpgPath = Path.Combine(outputDir, jpgName);
                AudioConverter.ConvertCoverToJpg(coverPath, jpgPath);
                pkg.Files[jpgName] = File.ReadAllBytes(jpgPath);
            }
            else if (coverPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     coverPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                pkg.Files[jpgName] = File.ReadAllBytes(coverPath);
            }
        }
        else
        {
            result.Warnings.Add("Kein Cover gefunden.");
        }

        // ── 6. Einzeldateien + STFS-Paket ───────────────────────────
        foreach (var (name, data) in pkg.Files)
            File.WriteAllBytes(Path.Combine(outputDir, name), data);

        Report("Erstelle STFS LIVE-Paket...");
        var stfsData = StfsWriter.CreatePackage(
            pkg.Files,
            $"\"{song.Title}\"",
            $"{song.Artist} - {song.Title}",
            titleId: 0x4D530888,
            contentType: 0x00000002);

        result.StfsFileName = StfsWriter.GetRequiredFileName(stfsData);
        result.StfsPath = Path.Combine(outputDir, result.StfsFileName);
        File.WriteAllBytes(result.StfsPath, stfsData);
        result.StfsSize = stfsData.Length;

        Report("Fertig.");
        return result;
    }

    /// <summary>
    /// Findet eine Mediendatei: erst den referenzierten Namen, dann per
    /// Endung im Ordner (Audio bevorzugt Nicht-Video-Formate).
    /// </summary>
    private static string? FindMediaFile(string dir, string referencedName,
        string[] extensions, bool preferAudio)
    {
        if (referencedName.Length > 0)
        {
            var p = Path.Combine(dir, referencedName);
            if (File.Exists(p)) return p;
        }

        var candidates = Directory.GetFiles(dir)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

        return preferAudio
            ? candidates.OrderBy(f => Path.GetExtension(f).ToLowerInvariant()
                is ".mp4" or ".mkv" or ".webm" ? 1 : 0).FirstOrDefault()
            : candidates.FirstOrDefault();
    }
}
