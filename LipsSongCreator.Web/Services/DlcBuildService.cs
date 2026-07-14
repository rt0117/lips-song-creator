using LipsSongExtractor;

namespace LipsSongCreator.Web.Services;

/// <summary>
/// Fuehrt die UltraStar-&gt;DLC-Komplettpipeline fuer die Web-UI aus.
/// Nimmt hochgeladene Dateien (txt + Audio/Video + Cover) entgegen,
/// legt sie in einem Temp-Ordner ab und baut das fertige STFS-Paket.
/// </summary>
public class DlcBuildService
{
    /// <summary>Status-Text des aktuellen Build-Schritts (fuer die UI).</summary>
    public string? CurrentStep { get; set; }

    /// <summary>Wird nach jedem Fortschritts-Update gefeuert (UI-Refresh).</summary>
    public event Action? ProgressChanged;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Vorbereitetes Projekt aus dem Wizard: Dateien liegen bereit,
    /// das DLC wird erst beim Export aus dem Editor gebaut.
    /// </summary>
    public Dictionary<string, byte[]>? PendingFiles { get; private set; }

    public bool HasPendingProject => PendingFiles != null;

    /// <summary>Projekt aus dem Wizard uebernehmen (ohne zu bauen).</summary>
    public void StageProject(Dictionary<string, byte[]> files)
    {
        PendingFiles = files;
    }

    /// <summary>Baut das DLC aus dem vorbereiteten Wizard-Projekt.</summary>
    public async Task<UltraStarDlcPipeline.Result> BuildPendingAsync()
    {
        if (PendingFiles == null)
            throw new InvalidOperationException("Kein vorbereitetes Projekt vorhanden.");
        return await BuildAsync(PendingFiles);
    }

    public void ClearPendingProject() => PendingFiles = null;

    /// <summary>Tool-Verfuegbarkeit pruefen (ffmpeg/xWMAEncode).</summary>
    public string? CheckTools() => AudioConverter.CheckTools();

    /// <summary>
    /// Baut das DLC aus hochgeladenen Dateien.
    /// </summary>
    /// <param name="files">Dateiname -> Inhalt (muss die .txt enthalten)</param>
    /// <returns>Pfad + Ergebnis-Infos; StfsPath zeigt in den Temp-Ordner</returns>
    public async Task<UltraStarDlcPipeline.Result> BuildAsync(
        Dictionary<string, byte[]> files)
    {
        var txtName = files.Keys.FirstOrDefault(k =>
            k.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Keine UltraStar .txt-Datei im Upload.");

        IsRunning = true;
        try
        {
            var workDir = Path.Combine(Path.GetTempPath(), $"lips_dlc_{Guid.NewGuid():N}");
            var srcDir = Path.Combine(workDir, "src");
            var outDir = Path.Combine(workDir, "out");
            Directory.CreateDirectory(srcDir);

            foreach (var (name, data) in files)
                await File.WriteAllBytesAsync(Path.Combine(srcDir, Path.GetFileName(name)), data);

            // Pipeline im Hintergrund-Thread (Video-Encoding dauert Minuten)
            var result = await Task.Run(() =>
                UltraStarDlcPipeline.Run(Path.Combine(srcDir, Path.GetFileName(txtName)), outDir,
                    step =>
                    {
                        CurrentStep = step;
                        ProgressChanged?.Invoke();
                    }));

            return result;
        }
        finally
        {
            IsRunning = false;
            CurrentStep = null;
        }
    }

    /// <summary>
    /// Liest das fertige Paket und raeumt den Temp-Ordner auf.
    /// </summary>
    public byte[] ReadAndCleanup(UltraStarDlcPipeline.Result result)
    {
        var bytes = File.ReadAllBytes(result.StfsPath);
        try
        {
            // workDir = zwei Ebenen ueber der STFS-Datei (out/..)
            var workDir = Path.GetDirectoryName(Path.GetDirectoryName(result.StfsPath));
            if (workDir != null && workDir.Contains("lips_dlc_"))
                Directory.Delete(workDir, recursive: true);
        }
        catch { /* Temp-Cleanup best effort */ }
        return bytes;
    }
}
