namespace LipsSongExtractor.Tests;

public class ToolLocatorTests
{
    [Fact]
    public void Find_LocatesToolsInRepoRoot_FromSubdirectory()
    {
        // Simuliert die Web-App: CurrentDirectory ist ein Unterordner
        // (LipsSongCreator.Web), tools/ liegt aber im Repo-Root.
        // ToolLocator muss nach oben wandern und es trotzdem finden.
        var repoRoot = FindRepoRoot();
        var toolsDir = Path.Combine(repoRoot, "tools");

        // Nur sinnvoll wenn tools/ existiert (lokale Dev-Umgebung)
        if (!Directory.Exists(toolsDir) ||
            !File.Exists(Path.Combine(toolsDir, "ffmpeg.exe")))
            return;

        var originalCwd = Environment.CurrentDirectory;
        try
        {
            // In einen Unterordner wechseln (wie die Web-App)
            Environment.CurrentDirectory = Path.Combine(repoRoot, "LipsSongCreator.Web");

            var found = ToolLocator.Find("ffmpeg", null);
            Assert.NotNull(found);
            Assert.EndsWith("ffmpeg.exe", found, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }

    [Fact]
    public void Find_ReturnsNullForNonexistentTool()
    {
        Assert.Null(ToolLocator.Find("definitely-not-a-real-tool-xyz", null));
    }

    [Fact]
    public void BuildStartInfo_WindowsExe_DirectOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var psi = ToolLocator.BuildStartInfo(@"C:\tools\test.exe", "-arg");
        Assert.Equal(@"C:\tools\test.exe", psi.FileName);
        Assert.Equal("-arg", psi.Arguments);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "Example")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new DirectoryNotFoundException("Repo-Root nicht gefunden");
    }
}
