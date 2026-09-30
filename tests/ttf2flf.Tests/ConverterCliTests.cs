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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InputsWithSameBaseName_FailBeforeWritingAnyOutput(bool withOutputDirectory)
    {
        using var temp = new TempDir();
        var first = CopyFixtureInto(temp.File("a"), "same.ttf");
        var second = CopyFixtureInto(temp.File("b"), "same.ttf");
        var outDir = temp.File("out");
        string[] args = withOutputDirectory ? [first, second, "-o", outDir] : [first, first];

        var (exitCode, output, error) = TestSupport.RunConverter(args);

        Assert.Equal(1, exitCode);
        Assert.Contains("would both write", error);
        Assert.Contains("same.flf", error);
        Assert.Equal("", output);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.flf", SearchOption.AllDirectories));
    }

    private static string CopyFixtureInto(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name);
        File.Copy(TestSupport.FixtureFont, target);
        return target;
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

    [Theory]
    [InlineData("█", false)]
    [InlineData("▀", false)]
    [InlineData("▄", false)]
    [InlineData("░", true)]
    [InlineData("▒", true)]
    [InlineData("▓", true)]
    [InlineData("█", true)]
    public void Hardblank_ThatAppearsInGlyphData_IsRejected(string hardblank, bool antiAliased)
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");
        string[] args = antiAliased
            ? [input, "--aa", "--hardblank", hardblank]
            : [input, "--hardblank", hardblank];

        var (exitCode, _, error) = TestSupport.RunConverter(args);

        Assert.Equal(1, exitCode);
        Assert.Contains("appears in", error);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.flf"));
    }

    [Theory]
    [InlineData("▀", true)]  // half blocks never appear in anti-aliased output
    [InlineData("░", false)] // shades never appear in pixel output
    public void Hardblank_FromTheOtherModesAlphabet_IsAccepted(string hardblank, bool antiAliased)
    {
        using var temp = new TempDir();
        var input = temp.CopyFixtureFont("Font.ttf");
        var target = temp.File("font.flf");
        string[] args = antiAliased
            ? [input, "--aa", "--hardblank", hardblank, "-o", target]
            : [input, "--hardblank", hardblank, "-o", target];

        var (exitCode, _, error) = TestSupport.RunConverter(args);

        Assert.True(exitCode == 0, error);
        Assert.StartsWith($"flf2a{hardblank} ", File.ReadLines(target).First());
        Assert.DoesNotContain(TestSupport.GlyphLines(target), line => line.Contains(hardblank));
    }

    [Fact]
    public void Encoders_EmitOnlyTheirDeclaredCharacters()
    {
        double[][] pixels = [[0, 0.2, 0.4, 0.6, 0.8, 1], [1, 0.8, 0.6, 0.4, 0.2, 1], [0, 1, 0, 1, 0, 1]];

        var halfBlock = string.Concat(HalfBlockEncoder.Encode(pixels));
        var shaded = string.Concat(BlockEncoder.Encode(pixels));

        Assert.All(halfBlock, c => Assert.Contains(c, HalfBlockEncoder.GlyphCharacters + " "));
        Assert.All(shaded, c => Assert.Contains(c, BlockEncoder.GlyphCharacters + " "));
        Assert.Equal((HalfBlockEncoder.GlyphCharacters + " ").Order(), halfBlock.Distinct().Order());
        Assert.Equal((BlockEncoder.GlyphCharacters + " ").Order(), shaded.Distinct().Order());
    }

    [Fact]
    public void Version_PrintsCalVer()
    {
        var (exitCode, output, _) = TestSupport.RunConverter("--version");

        Assert.Equal(0, exitCode);
        Assert.Matches(@"^ttf2flf \d{4}\.(?:[1-9]|1[0-2])\.(?:[1-9]|[12]\d|3[01])(?:[01]\d|2[0-3])[0-5]\d$", output.Trim());
    }
}
