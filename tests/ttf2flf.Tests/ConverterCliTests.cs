namespace ttf2flf.Tests;

public class ConverterCliTests
{
    [Fact]
    public void MultipleInputs_WithFlfOutputPath_FailBeforeWritingAnyOutput()
    {
        using var temp = new TempDir();
        var first = temp.CopyFixtureFont("First.ttf");
        var second = temp.CopyFixtureFont("Second.ttf");
        var combined = temp.File("combined.flf");

        var (exitCode, _, error) = TestSupport.RunConverter(first, second, "-o", combined);

        Assert.Equal(1, exitCode);
        Assert.Contains("multiple input fonts require --output to be a directory", error);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.flf", SearchOption.AllDirectories));
    }

    [Fact]
    public void MultipleInputs_WithExistingFileAsOutput_Fail()
    {
        using var temp = new TempDir();
        var first = temp.CopyFixtureFont("First.ttf");
        var second = temp.CopyFixtureFont("Second.ttf");
        var existingFile = temp.File("out");
        File.WriteAllText(existingFile, "keep");

        var (exitCode, _, error) = TestSupport.RunConverter(first, second, "-o", existingFile);

        Assert.Equal(1, exitCode);
        Assert.Contains("multiple input fonts require --output to be a directory", error);
        Assert.Equal("keep", File.ReadAllText(existingFile));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultipleInputs_WithDirectoryOutput_WriteOneFilePerInput(bool directoryExists)
    {
        using var temp = new TempDir();
        var first = temp.CopyFixtureFont("First.ttf");
        var second = temp.CopyFixtureFont("Second.ttf");
        var outDir = temp.File("out");
        if (directoryExists) Directory.CreateDirectory(outDir);

        var (exitCode, output, error) = TestSupport.RunConverter(first, second, "-o", outDir);

        Assert.True(exitCode == 0, error);
        var written = Directory.GetFiles(outDir, "*.flf").Select(Path.GetFileName).Order().ToArray();
        Assert.Equal(["First.flf", "Second.flf"], written);
        Assert.Equal(2, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void SingleInput_WithFlfOutputPath_WritesThatFile()
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");
        var target = temp.File("custom-name.flf");

        var (exitCode, output, error) = TestSupport.RunConverter(input, "-o", target);

        Assert.True(exitCode == 0, error);
        Assert.True(File.Exists(target));
        Assert.Equal(target, output.Trim());
    }

    [Theory]
    [InlineData("--no-trim")]
    [InlineData("--layout", "Smushed")]
    public void RemovedOptions_AreRejectedAsUnknown(params string[] option)
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");

        var (exitCode, _, error) = TestSupport.RunConverter([input, .. option]);

        Assert.Equal(1, exitCode);
        Assert.Contains($"unknown option '{option[0]}'", error);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.flf"));
    }

    [Fact]
    public void Version_PrintsCalVer()
    {
        var (exitCode, output, _) = TestSupport.RunConverter("--version");

        Assert.Equal(0, exitCode);
        Assert.Matches(@"^ttf2flf \d{4}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])(?:[01]\d|2[0-3])[0-5]\d$", output.Trim());
    }
}
