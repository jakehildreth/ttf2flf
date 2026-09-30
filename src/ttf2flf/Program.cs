using System.Reflection;
using SixLabors.Fonts;

namespace ttf2flf;

/// <summary>Glyph rasterization used by the converter. Tests substitute failing implementations.</summary>
public interface IGlyphRasterizer
{
    GlyphRenderer.MeasuredGlyph MeasurePixelPerfect(Font font, char character);

    Bitmap RenderAntiAliased(Font font, char character, int height, int maxWidth);
}

/// <summary>The production rasterizer: forwards to <see cref="GlyphRenderer"/>.</summary>
public sealed class GlyphRasterizer : IGlyphRasterizer
{
    public static readonly GlyphRasterizer Default = new();

    public GlyphRenderer.MeasuredGlyph MeasurePixelPerfect(Font font, char character) =>
        GlyphRenderer.MeasurePixelPerfect(font, character);

    public Bitmap RenderAntiAliased(Font font, char character, int height, int maxWidth) =>
        GlyphRenderer.RenderAntiAliased(font, character, height, maxWidth);
}

/// <summary>
/// Decides when per-glyph render failures make a conversion unusable. Isolated failures
/// become blank glyphs with a warning; the conversion fails when any core glyph
/// (A-Z, a-z, 0-9) fails or when more than <see cref="MaxFailedFraction"/> of the
/// required glyphs fail.
/// </summary>
public static class GlyphFailurePolicy
{
    public const double MaxFailedFraction = 0.10;

    public static bool IsCore(int charCode) =>
        charCode is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');

    /// <summary>Returns an error message when the failures are material; null otherwise.</summary>
    public static string? Evaluate(IReadOnlyCollection<int> failedCharCodes, int totalGlyphs)
    {
        var failedCore = failedCharCodes.Where(IsCore).ToList();
        if (failedCore.Count > 0)
        {
            var list = string.Join(", ", failedCore.Select(c => $"'{(char)c}'"));
            return $"{failedCore.Count} core glyph(s) failed to render ({list})";
        }

        if (failedCharCodes.Count > totalGlyphs * MaxFailedFraction)
        {
            return $"{failedCharCodes.Count} of {totalGlyphs} glyphs failed to render " +
                   $"(limit {MaxFailedFraction:P0})";
        }

        return null;
    }
}

public static class Program
{
    /// <summary>CalVer (yyyy.M.dHHmm) from assembly metadata; set in Directory.Build.props.</summary>
    public static string Version { get; } =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>Runs the CLI against explicit output streams and an optional rasterizer.</summary>
    public static int Run(
        string[] args, TextWriter output, TextWriter error, IGlyphRasterizer? rasterizer = null)
    {
        if (!CliParser.TryParse(args, output, error, out var options, out var parseExit))
        {
            return parseExit;
        }

        if (options.InputPaths.Count > 1 && options.OutputPath is { } outputPath
            && (outputPath.EndsWith(".flf", StringComparison.OrdinalIgnoreCase) || File.Exists(outputPath)))
        {
            error.WriteLine(
                $"error: multiple input fonts require --output to be a directory, got file path '{outputPath}'");
            return 1;
        }

        Action<string> verbose = options.Verbose
            ? error.WriteLine
            : _ => { };

        var converter = new Converter(options, output, error, verbose, rasterizer ?? GlyphRasterizer.Default);
        if (FindOutputCollision(options.InputPaths, converter) is { } collision)
        {
            error.WriteLine(collision);
            return 1;
        }

        var anyFailed = false;
        foreach (var inputPath in options.InputPaths)
        {
            if (!converter.ConvertOne(inputPath))
            {
                anyFailed = true;
            }
        }

        return anyFailed ? 1 : 0;
    }

    /// <summary>
    /// Resolves every input's destination before any conversion and reports the first
    /// pair that would write the same file (e.g. a/same.ttf and b/same.ttf into one
    /// directory). Paths compare case-insensitively on Windows and macOS, whose default
    /// file systems are case-insensitive. Inputs whose path is invalid are skipped here;
    /// ConvertOne reports them.
    /// </summary>
    private static string? FindOutputCollision(IReadOnlyList<string> inputPaths, Converter converter)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var destinations = new Dictionary<string, string>(comparer);
        foreach (var inputPath in inputPaths)
        {
            string destination;
            try
            {
                destination = Path.GetFullPath(converter.ResolveOutputFile(Path.GetFullPath(inputPath)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (destinations.TryGetValue(destination, out var firstInput))
            {
                return $"error: inputs '{firstInput}' and '{inputPath}' would both write '{destination}'; " +
                       "rename one input or convert them separately";
            }

            destinations[destination] = inputPath;
        }

        return null;
    }

    private sealed class Converter(
        CliOptions options,
        TextWriter output,
        TextWriter error,
        Action<string> verbose,
        IGlyphRasterizer rasterizer)
    {
        public bool ConvertOne(string inputPath)
        {
            string resolvedPath;
            try
            {
                resolvedPath = Path.GetFullPath(inputPath);
                if (!File.Exists(resolvedPath))
                {
                    error.WriteLine($"error: file not found: {inputPath}");
                    return false;
                }

                if (!resolvedPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                    && !resolvedPath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                {
                    error.WriteLine($"error: file must be a TrueType font (.ttf or .otf): {inputPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                error.WriteLine($"error: invalid path '{inputPath}': {ex.Message}");
                return false;
            }

            var outputFile = ResolveOutputFile(resolvedPath);

            try
            {
                verbose($"[+] Processing: {resolvedPath}");

                var fontCollection = new FontCollection();
                var fontFamily = fontCollection.Add(resolvedPath);

                var (isCompatible, warnings, fontName) =
                    FontCompatibility.Check(fontFamily, resolvedPath);
                foreach (var warning in warnings)
                {
                    error.WriteLine(warning);
                }

                if (!isCompatible)
                {
                    error.WriteLine("[!] Font may not produce usable output. Continuing anyway...");
                }

                var pixelPerfect = !options.AntiAliased;
                var failed = new List<int>();
                var (outputHeight, characterData) = pixelPerfect
                    ? RenderPixelPerfect(fontFamily, resolvedPath, failed)
                    : RenderAntiAliased(fontFamily, failed);

                if (GlyphFailurePolicy.Evaluate(failed, FlfWriter.RequiredCharacters.Length) is { } failure)
                {
                    error.WriteLine($"error: conversion failed for '{inputPath}': {failure}; no output written");
                    return false;
                }

                var formatWidth = options.Monospace
                    ? characterData.Values.Max(b => b.Width)
                    : 0;

                // Encode to FLF rows.
                verbose("[+] Converting to FLF format...");
                var flfCharacters = new Dictionary<int, string[]>();
                foreach (var charCode in FlfWriter.RequiredCharacters)
                {
                    var bitmap = characterData[charCode];
                    var asciiRows = pixelPerfect
                        ? HalfBlockEncoder.Encode(bitmap.Pixels)
                        : BlockEncoder.Encode(bitmap.Pixels);
                    flfCharacters[charCode] =
                        FlfWriter.FormatCharacter(asciiRows, formatWidth);
                }

                var outputDirectory = Path.GetDirectoryName(outputFile);
                if (!string.IsNullOrEmpty(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var header = FlfWriter.WriteFile(
                    outputFile,
                    flfCharacters,
                    outputHeight,
                    options.Hardblank,
                    fontName,
                    resolvedPath,
                    Version);

                verbose($"[+] Header: {header}");
                output.WriteLine(outputFile);
                verbose("[+] Conversion complete");
                return true;
            }
            catch (Exception ex)
            {
                error.WriteLine($"error: conversion failed for '{inputPath}': {ex.Message}");
                return false;
            }
        }

        public string ResolveOutputFile(string resolvedPath)
        {
            if (options.OutputPath is null)
            {
                return Path.ChangeExtension(resolvedPath, ".flf");
            }

            // A directory target: an existing directory, a trailing separator, or any
            // path when converting several inputs (validated up front in Run).
            if (Directory.Exists(options.OutputPath)
                || options.OutputPath.EndsWith(Path.DirectorySeparatorChar)
                || options.OutputPath.EndsWith(Path.AltDirectorySeparatorChar)
                || options.InputPaths.Count > 1)
            {
                var baseName = Path.GetFileNameWithoutExtension(resolvedPath);
                return Path.Combine(options.OutputPath, $"{baseName}.flf");
            }

            return options.OutputPath;
        }

        private (int OutputHeight, Dictionary<int, Bitmap> Characters) RenderPixelPerfect(
            FontFamily fontFamily, string resolvedPath, List<int> failed)
        {
            var grid = FontGridDetector.Detect(
                fontFamily,
                explicitHeight: options.Height ?? 0,
                unitsPerPixel: options.UnitsPerPixel ?? 0,
                verbose: verbose);
            var gridSize = grid.Height;
            verbose($"[+] Design grid: {gridSize}px");

            // Render at the calibrated/computed size (outline em >> design grid), NOT the
            // grid. --render-size wins; else the calibration table; else scale+nudge.
            var renderSize = options.RenderSize
                ?? FontGridDetector.DetectRenderSize(fontFamily, gridSize, resolvedPath, verbose);
            verbose($"[+] Rendering at {renderSize}px (grid {gridSize})");
            var font = fontFamily.CreateFont(renderSize);

            // Width for --monospace only: advance-based at the render size. Glyphs are
            // measured at their own tight bbox width (proportional).
            var detectedWidth = FontGridDetector.GetWidth(font);

            // Pass 1: measure each glyph's tight bbox (span + bottom edge).
            var measured = new Dictionary<int, GlyphRenderer.MeasuredGlyph>();
            var maxSpan = 0;
            var globalMaxBottom = int.MinValue;
            verbose($"[+] Measuring {FlfWriter.RequiredCharacters.Length} characters...");
            foreach (var charCode in FlfWriter.RequiredCharacters)
            {
                var character = (char)charCode;
                try
                {
                    var glyph = rasterizer.MeasurePixelPerfect(font, character);
                    measured[charCode] = glyph;
                    if (glyph.Span > maxSpan) maxSpan = glyph.Span;
                    if (!glyph.IsEmpty && glyph.ContentBottomFromOrigin > globalMaxBottom)
                    {
                        globalMaxBottom = glyph.ContentBottomFromOrigin;
                    }
                }
                catch (Exception ex)
                {
                    ReportGlyphFailure(charCode, ex, failed);
                    measured[charCode] = new GlyphRenderer.MeasuredGlyph([], 1, 0, 0, 1, true);
                }
            }

            // Height = ceil(tallest glyph's vertical pixel span / 2). Odd spans round UP.
            if (maxSpan < 1) maxSpan = 1;
            var outputHeight = (int)Math.Ceiling(maxSpan / 2.0);
            var canvasPixels = outputHeight * 2;
            verbose(
                $"[+] Tallest glyph span: {maxSpan}px -> {outputHeight} rows (canvas {canvasPixels}px)");

            // Pass 2: bottom-align each glyph on the shared canvas (global max bottom ->
            // last pixel row), preserving every glyph's internal gaps and true offsets.
            // 1px right spacer per glyph: advance = own content width + 1, giving every
            // letter a consistent 1px gap under FullWidth layout. --monospace overrides.
            var targetWidth = options.Monospace ? detectedWidth : 0;
            var characters = new Dictionary<int, Bitmap>();
            foreach (var charCode in FlfWriter.RequiredCharacters)
            {
                characters[charCode] = GlyphRenderer.PlaceOnCanvas(
                    measured[charCode], canvasPixels, globalMaxBottom, targetWidth, rightSpacer: 1);
            }

            return (outputHeight, characters);
        }

        private (int OutputHeight, Dictionary<int, Bitmap> Characters) RenderAntiAliased(
            FontFamily fontFamily, List<int> failed)
        {
            var height = options.Height ?? 8;
            var fontSize = Math.Max(48, height * 8);
            var font = fontFamily.CreateFont(fontSize);
            verbose($"[+] Anti-aliased mode: render {fontSize}px -> {height} row FLF");

            var characters = new Dictionary<int, Bitmap>();
            var maxWidth = 0;
            verbose($"[+] Rendering {FlfWriter.RequiredCharacters.Length} characters...");
            foreach (var charCode in FlfWriter.RequiredCharacters)
            {
                var bitmap = RenderAntiAliasedGlyph(font, charCode, height, 0, failed);
                characters[charCode] = bitmap;
                if (bitmap.Width > maxWidth) maxWidth = bitmap.Width;
            }

            if (options.Monospace && maxWidth > 0)
            {
                // Second pass at the shared width; glyphs that already failed keep their
                // blank placeholder (FormatCharacter pads it to the monospace width).
                var alreadyFailed = failed.ToHashSet();
                foreach (var charCode in FlfWriter.RequiredCharacters.Where(c => !alreadyFailed.Contains(c)))
                {
                    characters[charCode] = RenderAntiAliasedGlyph(font, charCode, height, maxWidth, failed);
                }
            }

            return (height, characters);
        }

        private Bitmap RenderAntiAliasedGlyph(Font font, int charCode, int height, int maxWidth, List<int> failed)
        {
            try
            {
                return rasterizer.RenderAntiAliased(font, (char)charCode, height, maxWidth);
            }
            catch (Exception ex)
            {
                ReportGlyphFailure(charCode, ex, failed);
                var width = Math.Max(1, maxWidth);
                return new Bitmap
                {
                    Width = width,
                    Height = height,
                    Pixels = Enumerable.Range(0, height).Select(_ => new double[width]).ToArray(),
                };
            }
        }

        private void ReportGlyphFailure(int charCode, Exception ex, List<int> failed)
        {
            failed.Add(charCode);
            error.WriteLine($"[!] Failed to render character {charCode} ({(char)charCode}): {ex.Message}");
        }
    }
}
