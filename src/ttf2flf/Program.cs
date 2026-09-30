using SixLabors.Fonts;

namespace ttf2flf;

public static class Program
{
    private const string Version = "1.0.0";

    public static int Main(string[] args)
    {
        if (!CliParser.TryParse(args, out var options, out var parseExit))
        {
            return parseExit;
        }

        Action<string> verbose = options.Verbose
            ? msg => Console.Error.WriteLine(msg)
            : _ => { };

        var anyFailed = false;
        foreach (var inputPath in options.InputPaths)
        {
            if (!ConvertOne(inputPath, options, verbose))
            {
                anyFailed = true;
            }
        }

        return anyFailed ? 1 : 0;
    }

    private static bool ConvertOne(string inputPath, CliOptions options, Action<string> verbose)
    {
        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(inputPath);
            if (!File.Exists(resolvedPath))
            {
                Console.Error.WriteLine($"error: file not found: {inputPath}");
                return false;
            }

            if (!resolvedPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !resolvedPath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"error: file must be a TrueType font (.ttf or .otf): {inputPath}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: invalid path '{inputPath}': {ex.Message}");
            return false;
        }

        string outputFile;
        if (options.OutputPath is null)
        {
            outputFile = Path.ChangeExtension(resolvedPath, ".flf");
        }
        else if (Directory.Exists(options.OutputPath)
                 || options.OutputPath.EndsWith(Path.DirectorySeparatorChar)
                 || options.OutputPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            var baseName = Path.GetFileNameWithoutExtension(resolvedPath);
            outputFile = Path.Combine(options.OutputPath, $"{baseName}.flf");
        }
        else if (options.InputPaths.Count > 1
                 && !options.OutputPath.EndsWith(".flf", StringComparison.OrdinalIgnoreCase))
        {
            // Multi-input with a non-.flf output: treat as a directory-to-be.
            var baseName = Path.GetFileNameWithoutExtension(resolvedPath);
            outputFile = Path.Combine(options.OutputPath, $"{baseName}.flf");
        }
        else
        {
            outputFile = options.OutputPath;
        }

        try
        {
            verbose($"[+] Processing: {resolvedPath}");

            var fontCollection = new FontCollection();
            var fontFamily = fontCollection.Add(resolvedPath);

            var (isCompatible, warnings, fontName) =
                FontCompatibility.Check(fontFamily, resolvedPath);
            foreach (var warning in warnings)
            {
                Console.Error.WriteLine(warning);
            }

            if (!isCompatible)
            {
                Console.Error.WriteLine("[!] Font may not produce usable output. Continuing anyway...");
            }

            var pixelPerfect = !options.AntiAliased;
            int outputHeight;
            Font font;
            var characterData = new Dictionary<int, Bitmap>();

            if (pixelPerfect)
            {
                var grid = FontGridDetector.Detect(
                    fontFamily,
                    explicitHeight: options.Height ?? 0,
                    unitsPerPixel: options.UnitsPerPixel ?? 0,
                    verbose: verbose);
                var gridSize = grid.Height;
                verbose($"[+] Design grid: {gridSize}px");

                // Render at the calibrated/computed size (outline em >> design grid), NOT the
                // grid. --render-size wins; else render_sizes.json; else scale+nudge.
                var renderSize = options.RenderSize
                    ?? FontGridDetector.DetectRenderSize(fontFamily, gridSize, resolvedPath, verbose);
                verbose($"[+] Rendering at {renderSize}px (grid {gridSize})");
                font = fontFamily.CreateFont(renderSize);

                // Width for --monospace only: advance-based at the render size. Glyphs are
                // measured at their own tight bbox width (proportional); forcing a uniform
                // width here is what caused the spaced-apart output. detectedWidth is used
                // solely as the monospace target width below.
                var detectedWidth = FontGridDetector.GetWidth(font);
                // Pass 1: measure each glyph's tight bbox (span + bottom edge) at the grid.
                var measured = new Dictionary<int, GlyphRenderer.MeasuredGlyph>();
                var maxSpan = 0;
                var maxWidth = 0;
                var globalMaxBottom = int.MinValue;
                verbose($"[+] Measuring {FlfWriter.RequiredCharacters.Length} characters...");
                foreach (var charCode in FlfWriter.RequiredCharacters)
                {
                    var character = (char)charCode;
                    try
                    {
                        var glyph = GlyphRenderer.MeasurePixelPerfect(
                            font, character, width: 0);
                        measured[charCode] = glyph;
                        if (glyph.Span > maxSpan) maxSpan = glyph.Span;
                        if (glyph.Width > maxWidth) maxWidth = glyph.Width;
                        if (!glyph.IsEmpty && glyph.ContentBottomFromOrigin > globalMaxBottom)
                        {
                            globalMaxBottom = glyph.ContentBottomFromOrigin;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[!] Failed to render character {charCode} ({character}): {ex.Message}");
                        measured[charCode] = new GlyphRenderer.MeasuredGlyph(
                            [], 1, 0, 0, 1, true);
                    }
                }

                // Height = ceil(tallest glyph's vertical pixel span / 2). Odd spans round UP.
                if (maxSpan < 1) maxSpan = 1;
                outputHeight = (int)Math.Ceiling(maxSpan / 2.0);
                var canvasPixels = outputHeight * 2;
                verbose(
                    $"[+] Tallest glyph span: {maxSpan}px -> {outputHeight} rows (canvas {canvasPixels}px)");

                // Pass 2: bottom-align each glyph on the shared canvas (global max bottom ->
                // last pixel row), preserving every glyph's internal gaps and true offsets.
                var targetWidth = options.Monospace ? detectedWidth : 0;
                foreach (var charCode in FlfWriter.RequiredCharacters)
                {
                    var bitmap = GlyphRenderer.PlaceOnCanvas(
                        measured[charCode], canvasPixels, globalMaxBottom, targetWidth);
                    characterData[charCode] = bitmap;
                }
            }
            else
            {
                var height = options.Height ?? 8;
                var fontSize = Math.Max(48, height * 8);
                outputHeight = height;
                font = fontFamily.CreateFont(fontSize);
                verbose($"[+] Anti-aliased mode: render {fontSize}px -> {outputHeight} row FLF");

                var maxWidth = 0;
                verbose($"[+] Rendering {FlfWriter.RequiredCharacters.Length} characters...");
                foreach (var charCode in FlfWriter.RequiredCharacters)
                {
                    var character = (char)charCode;
                    try
                    {
                        var bitmap = GlyphRenderer.RenderAntiAliased(font, character, height);
                        characterData[charCode] = bitmap;
                        if (bitmap.Width > maxWidth) maxWidth = bitmap.Width;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[!] Failed to render character {charCode} ({character}): {ex.Message}");
                        characterData[charCode] = new Bitmap
                        {
                            Width = 1,
                            Height = height,
                            Pixels = Enumerable.Range(0, height)
                                .Select(_ => new double[1])
                                .ToArray(),
                        };
                    }
                }

                if (options.Monospace && maxWidth > 0)
                {
                    foreach (var charCode in FlfWriter.RequiredCharacters)
                    {
                        characterData[charCode] = GlyphRenderer.RenderAntiAliased(
                            font, (char)charCode, height, maxWidth);
                    }
                }
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
                    : BlockEncoder.Encode(bitmap.Pixels, options.Hardblank);
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
                options.Layout,
                fontName,
                resolvedPath,
                Version);

            verbose($"[+] Header: {header}");
            Console.WriteLine(outputFile);
            verbose("[+] Conversion complete");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: conversion failed for '{inputPath}': {ex.Message}");
            return false;
        }
    }
}
