using System.Diagnostics;

namespace LipsSongExtractor;

/// <summary>
/// Findet externe Tools (ffmpeg, xWMAEncode, yt-dlp) plattformuebergreifend.
///
/// Suchreihenfolge:
///   1. Umgebungsvariable (z.B. FFMPEG_PATH) mit vollem Pfad
///   2. tools/-Ordner: ab CurrentDirectory UND AppContext.BaseDirectory
///      jeweils bis zu 6 Ebenen NACH OBEN wandernd (wichtig: die Web-App
///      laeuft in LipsSongCreator.Web/, tools/ liegt im Repo-Root!)
///   3. PATH
///
/// Linux: Native Binaries (ffmpeg, yt-dlp) werden ohne .exe gesucht.
/// Windows-only-Tools (xWMAEncode.exe) laufen unter Linux via wine.
/// </summary>
public static class ToolLocator
{
    /// <summary>
    /// Sucht ein Tool. Gibt den vollen Pfad zurueck oder null.
    /// </summary>
    /// <param name="baseName">Tool-Name ohne Endung (z.B. "ffmpeg")</param>
    /// <param name="envVar">Optionale Umgebungsvariable mit vollem Pfad</param>
    /// <param name="windowsOnly">True wenn es das Tool nur als .exe gibt (z.B. xWMAEncode)</param>
    public static string? Find(string baseName, string? envVar = null, bool windowsOnly = false)
    {
        // 1. Umgebungsvariable
        if (envVar != null)
        {
            var envPath = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
                return envPath;
        }

        // Kandidaten-Dateinamen je Plattform
        var names = OperatingSystem.IsWindows() || windowsOnly
            ? new[] { baseName + ".exe" }
            : new[] { baseName, baseName + ".exe" }; // .exe via wine moeglich

        // 2. tools/-Ordner: von beiden Startpunkten nach oben wandern
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = start;
            for (var i = 0; i < 6 && dir != null; i++)
            {
                foreach (var name in names)
                {
                    var toolsPath = Path.Combine(dir, "tools", name);
                    if (File.Exists(toolsPath)) return toolsPath;

                    var directPath = Path.Combine(dir, name);
                    if (File.Exists(directPath)) return directPath;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        // 3. PATH
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var pathDir in pathDirs)
        {
            foreach (var name in names)
            {
                var p = Path.Combine(pathDir.Trim(), name);
                if (File.Exists(p)) return p;
            }
        }

        return null;
    }

    /// <summary>
    /// Startet ein Tool. Windows-Binaries (.exe) laufen auf Linux
    /// automatisch via wine (falls installiert).
    /// </summary>
    public static ProcessStartInfo BuildStartInfo(string exePath, string arguments)
    {
        // .exe auf Nicht-Windows: via wine starten
        if (!OperatingSystem.IsWindows() &&
            exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = "wine",
                Arguments = $"\"{exePath}\" {arguments}",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
        }

        return new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    /// <summary>True wenn wine verfuegbar ist (fuer .exe-Tools auf Linux).</summary>
    public static bool IsWineAvailable()
    {
        if (OperatingSystem.IsWindows()) return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wine",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }
}
