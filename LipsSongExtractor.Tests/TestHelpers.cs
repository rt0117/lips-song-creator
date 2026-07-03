namespace LipsSongExtractor.Tests;

/// <summary>
/// Hilfsfunktionen um die Example-Dateien relativ zum Testprojekt zu finden.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Gibt den Pfad zum Example-Ordner zurück, egal ob Tests aus bin/ oder Root laufen.
    /// </summary>
    public static string ExampleDir
    {
        get
        {
            // Versuche ab dem aktuellen Verzeichnis aufwärts den Example-Ordner zu finden
            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "Example");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir)!;
            }

            throw new DirectoryNotFoundException(
                "Example-Ordner nicht gefunden. Stelle sicher, dass die Tests " +
                "innerhalb des Repository-Root ausgeführt werden.");
        }
    }

    public static string CaliforniaLoveX360 =>
        Path.Combine(ExampleDir, "California Love", "California Love.X360");

    public static string CaliforniaLoveLyricX360 =>
        Path.Combine(ExampleDir, "California Love", "California Love_Lyric.X360");
}
