using System.Diagnostics;
using System.Text;

namespace ttf2flf.Tests;

/// <summary>Shared fixtures: temp directories, the OFL test font, CLI runs, and FIGlet.</summary>
internal static class TestSupport
{
    public static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string FixtureFont => FixturePath("PressStart2P-Regular.ttf");

    public static (int ExitCode, string Output, string Error) RunConverter(params string[] args) =>
        RunConverterWith(null, args);

    public static (int ExitCode, string Output, string Error) RunConverterWith(
        IGlyphRasterizer? rasterizer, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = Program.Run(args, output, error, rasterizer);
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Header fields after the signature: height, baseline, maxlen, oldlayout, comments, ...</summary>
    public static int[] HeaderFields(string flfPath) =>
        File.ReadLines(flfPath).First()[6..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();

    /// <summary>Glyph-data lines: everything after the header and comment block.</summary>
    public static string[] GlyphLines(string flfPath)
    {
        var lines = File.ReadAllLines(flfPath, Encoding.UTF8);
        var commentLines = HeaderFields(flfPath)[4];
        return lines.Skip(1 + commentLines).ToArray();
    }

    /// <summary>
    /// Renders text with FIGlet. Skips the test when figlet is not installed, unless
    /// REQUIRE_FIGLET=1 (set by CI on Linux and macOS), which turns that into a failure.
    /// </summary>
    public static string[] Figlet(string fontPath, string text)
    {
        var figlet = FindOnPath(OperatingSystem.IsWindows() ? "figlet.exe" : "figlet");
        if (figlet is null)
        {
            Assert.False(
                Environment.GetEnvironmentVariable("REQUIRE_FIGLET") == "1",
                "figlet is required (REQUIRE_FIGLET=1) but was not found on PATH");
            Assert.Skip("figlet is not installed");
        }

        var startInfo = new ProcessStartInfo(figlet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-w", "10000", "-f", fontPath, text })
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"figlet failed: {stderr}");

        return stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')
            .Select(line => line.TrimEnd())
            .ToArray();
    }

    private static string? FindOnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, executable))
            .FirstOrDefault(File.Exists);
}

/// <summary>A temporary directory deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ttf2flf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Copies the fixture font into this directory under a new name.</summary>
    public string CopyFixtureFont(string name)
    {
        var target = File(name);
        System.IO.File.Copy(TestSupport.FixtureFont, target);
        return target;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp cleaner handles leftovers.
        }
    }
}
