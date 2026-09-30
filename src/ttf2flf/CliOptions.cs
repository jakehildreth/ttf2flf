namespace ttf2flf;

public enum LayoutMode
{
    FullWidth,
    Kerned,
    Smushed,
}

public sealed class CliOptions
{
    public List<string> InputPaths { get; } = new();
    public string? OutputPath { get; set; }
    public int? Height { get; set; }
    public bool AntiAliased { get; set; }
    public bool Monospace { get; set; }
    public bool NoTrim { get; set; }
    public char Hardblank { get; set; } = '$';
    public LayoutMode Layout { get; set; } = LayoutMode.FullWidth;
    public int? UnitsPerPixel { get; set; }
    public int? RenderSize { get; set; }
    public bool Verbose { get; set; }
}

public static class CliParser
{
    private const string Usage = """
        ttf2flf - Convert TrueType fonts to FIGlet font files (.flf)
        Usage:
          ttf2flf <path.ttf> [<path2.ttf> ...] [options]
        Options:
          -o, --output <file|dir>     Output .flf path or directory (default: input path, .flf ext)
              --height <4..64>        Force pixel size (skip auto-detection); with --aa = terminal rows (default 8)
              --aa                    Anti-aliased mode (░▒▓█ blocks instead of pixel-perfect half blocks)
              --monospace             Pad all glyphs to max advance width
              --no-trim               Accepted for compatibility; no effect (height is set by the tallest glyph's pixel span, no trim pass)
              --hardblank <char>      Hardblank character (default: $)
              --layout <mode>         FullWidth | Kerned | Smushed (default: FullWidth)
              --units-per-pixel <n>   Override detection: grid = UnitsPerEm / n
              --render-size <px>      Explicit render size (wins over detection; skips calibration)
          -v, --verbose               Verbose output
          -h, --help                  Show this help
        """;

    /// <summary>Parses args. Returns false when parsing failed or help was shown.</summary>
    public static bool TryParse(string[] args, out CliOptions options, out int exitCode)
    {
        options = new CliOptions();
        exitCode = 0;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            string? NextValue(string name)
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine($"error: {name} requires a value");
                    return null;
                }

                return args[++i];
            }

            switch (arg)
            {
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    exitCode = 0;
                    return false;
                case "-o" or "--output":
                    var outPath = NextValue(arg);
                    if (outPath is null) goto Fail;
                    options.OutputPath = outPath;
                    break;
                case "--height":
                    var h = NextValue(arg);
                    if (h is null) goto Fail;
                    if (!int.TryParse(h, out var height) || height < 4 || height > 64)
                    {
                        Console.Error.WriteLine($"error: --height must be an integer in 4..64, got '{h}'");
                        goto Fail;
                    }

                    options.Height = height;
                    break;
                case "--aa":
                    options.AntiAliased = true;
                    break;
                case "--monospace":
                    options.Monospace = true;
                    break;
                case "--no-trim":
                    options.NoTrim = true;
                    break;
                case "--hardblank":
                    var hb = NextValue(arg);
                    if (hb is null) goto Fail;
                    if (hb.Length != 1 || hb[0] is ' ' or '\r' or '\n' or '\0')
                    {
                        Console.Error.WriteLine("error: --hardblank must be a single character (not space/CR/LF/NUL)");
                        goto Fail;
                    }

                    options.Hardblank = hb[0];
                    break;
                case "--layout":
                    var lm = NextValue(arg);
                    if (lm is null) goto Fail;
                    if (!Enum.TryParse(lm, ignoreCase: true, out LayoutMode layout))
                    {
                        Console.Error.WriteLine($"error: --layout must be FullWidth|Kerned|Smushed, got '{lm}'");
                        goto Fail;
                    }

                    options.Layout = layout;
                    break;
                case "--units-per-pixel":
                    var upp = NextValue(arg);
                    if (upp is null) goto Fail;
                    if (!int.TryParse(upp, out var unitsPerPixel) || unitsPerPixel < 1 || unitsPerPixel > 1000)
                    {
                        Console.Error.WriteLine($"error: --units-per-pixel must be an integer in 1..1000, got '{upp}'");
                        goto Fail;
                    }

                    options.UnitsPerPixel = unitsPerPixel;
                    break;
                case "--render-size":
                    var rs = NextValue(arg);
                    if (rs is null) goto Fail;
                    if (!int.TryParse(rs, out var renderSize) || renderSize < 1 || renderSize > 1024)
                    {
                        Console.Error.WriteLine($"error: --render-size must be an integer in 1..1024, got '{rs}'");
                        goto Fail;
                    }

                    options.RenderSize = renderSize;
                    break;
                case "-v" or "--verbose":
                    options.Verbose = true;
                    break;
                case { } when arg.StartsWith('-'):
                    Console.Error.WriteLine($"error: unknown option '{arg}'");
                    goto Fail;
                default:
                    options.InputPaths.Add(arg!);
                    break;
            }
        }

        if (options.InputPaths.Count == 0)
        {
            Console.Error.WriteLine("error: at least one input font path is required");
            goto Fail;
        }

        return true;

        Fail:
        Console.Error.WriteLine("Run 'ttf2flf --help' for usage.");
        exitCode = 1;
        return false;
    }
}
