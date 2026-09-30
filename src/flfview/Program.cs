using flfview;

// (dir, tier): the curated corpus is PNG-verified; Font Book additions are clean
// except the decorative/stroked ones flagged approximate in FlfFont.ApproximateFonts.
(string Dir, string Tier)[] defaultFontDirs =
[
    ("/Users/jhildreth/Repos/ttf2flf/Corpus/OutputFLF", "verified"),
    ("/Users/jhildreth/Repos/ttf2flf/Corpus/FontBookFLF", "clean"),
];

// ---------------------------------------------------------------- args
(string Dir, string Tier)[] fontDirs = [.. defaultFontDirs];
string? renderWord = null;
string? fontName = null;
bool renderAll = false;
bool listFonts = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dir" when i + 1 < args.Length:
            fontDirs = [(args[++i], "custom")];
            break;
        case "--render" when i + 1 < args.Length:
            renderWord = args[++i];
            break;
        case "--font" when i + 1 < args.Length:
            fontName = args[++i];
            break;
        case "--all":
            renderAll = true;
            break;
        case "--fonts":
            listFonts = true;
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintUsage();
            return 2;
    }
}

static void PrintUsage() => Console.WriteLine("""
    flfview — interactive half-block FIGlet font previewer

    Usage:
      flfview [--dir <path>]                          interactive mode
      flfview --render "<word>" --font "<name>"       one-shot: render word in one font
      flfview --render "<word>" --all                 one-shot: render word in every font
      flfview --fonts                                 list font names and exit

    Interactive keys:
      printable chars   append to the word
      Backspace         delete last char
      Enter             re-render
      Up/Down, PgUp/PgDn, Tab/Shift+Tab
                        move active font selection (1 / 10 rows) and re-render
      /all <word>       render word in all fonts (paged, any key = next, q = stop)
      /fonts            list font names (paged)
      Esc or /quit      exit
    """);

// ---------------------------------------------------------------- load fonts
var fonts = new List<FlfFont>();
var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach (var (dir, tier) in fontDirs)
{
    if (!Directory.Exists(dir))
    {
        Console.Error.WriteLine($"warning: font directory not found: {dir}");
        continue;
    }

    foreach (var path in Directory.EnumerateFiles(dir, "*.flf").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
    {
        if (!seenNames.Add(Path.GetFileNameWithoutExtension(path)))
            continue; // dedupe by font name across dirs
        try
        {
            var font = FlfFont.Load(path);
            // h/p variants of verified families convert cleanly at the calibrated render
            // size (16) and Jake confirmed they look right -> verified.
            font.Tier = FlfFont.ApproximateFonts.Contains(font.Name) ? "approximate"
                : System.Text.RegularExpressions.Regex.IsMatch(font.Name, @"\d[hp]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "verified"
                : tier;
            fonts.Add(font);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: skipping {Path.GetFileName(path)}: {ex.Message}");
        }
    }
}

if (fonts.Count == 0)
{
        Console.Error.WriteLine($"No usable .flf fonts found in {string.Join(", ", fontDirs)}");
    return 1;
}

if (listFonts)
{
    foreach (var f in fonts)
        Console.WriteLine($"{f.Tier,-12} {f.Name}");
    return 0;
}

// ---------------------------------------------------------------- one-shot mode
if (renderWord is not null)
{
    if (renderAll)
    {
        foreach (var font in fonts)
        {
            Console.WriteLine($"=== {font.Name} [{font.Tier}] (height {font.Height}) ===");
            Console.WriteLine(Renderer.RenderToString(font, renderWord));
            Console.WriteLine();
        }
        return 0;
    }

    var selected = fontName is null
        ? fonts[0]
        : fonts.FirstOrDefault(f =>
              string.Equals(f.Name, fontName, StringComparison.OrdinalIgnoreCase))
          ?? fonts.FirstOrDefault(f =>
              f.Name.Contains(fontName, StringComparison.OrdinalIgnoreCase));

    if (selected is null)
    {
        Console.Error.WriteLine($"Font not found: {fontName}");
        return 1;
    }

    Console.WriteLine(Renderer.RenderToString(selected, renderWord));
    return 0;
}

// ---------------------------------------------------------------- interactive mode
return RunInteractive(fonts);

static int RunInteractive(IReadOnlyList<FlfFont> allFonts)
{
    var word = "";
    var fontIndex = 0;
    var tierFilter = "all"; // all | verified | clean | approximate
    var status = "Type a word, Enter to render. /help for commands. Esc quits.";
    var fonts = FilterByTier(allFonts, tierFilter);

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.CursorVisible = true;
        Environment.Exit(0);
    };

    while (true)
    {
        DrawScreen(fonts, ref fontIndex, word, status, tierFilter);
        status = "";

        var key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Escape)
            break;

        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                if (word.Length > 0)
                    word = word[..^1];
                continue;

            case ConsoleKey.Enter:
            {
                if (word.StartsWith('/'))
                {
                    if (word.StartsWith("/tier", StringComparison.OrdinalIgnoreCase))
                    {
                        // /tier <all|verified|clean|approximate> rebuilds the view.
                        var arg = word.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        var want = arg.Length > 1 ? arg[1].ToLowerInvariant() : "all";
                        if (want is "all" or "verified" or "clean" or "approximate")
                        {
                            tierFilter = want;
                            fonts = FilterByTier(allFonts, tierFilter);
                            fontIndex = 0;
                            status = $"Filter: {tierFilter} ({fonts.Count} fonts).";
                        }
                        else
                        {
                            status = "Usage: /tier all|verified|clean|approximate";
                        }
                        word = "";
                    }
                    else
                    {
                        word = RunCommand(word, fonts, ref fontIndex, ref status);
                    }
                }
                continue;
            }

            case ConsoleKey.UpArrow:
                fontIndex = Wrap(fontIndex - 1, fonts.Count);
                continue;
            case ConsoleKey.DownArrow:
                fontIndex = Wrap(fontIndex + 1, fonts.Count);
                continue;
            case ConsoleKey.PageUp:
                fontIndex = Wrap(fontIndex - 10, fonts.Count);
                continue;
            case ConsoleKey.PageDown:
                fontIndex = Wrap(fontIndex + 10, fonts.Count);
                continue;
            case ConsoleKey.Tab:
                fontIndex = Wrap(
                    fontIndex + ((key.Modifiers & ConsoleModifiers.Shift) != 0 ? -1 : 1),
                    fonts.Count);
                continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            word += key.KeyChar;
            continue;
        }
    }

    Console.Clear();
    Console.CursorVisible = true;
    return 0;
}

static int Wrap(int index, int count) => count == 0 ? 0 : ((index % count) + count) % count;

static List<FlfFont> FilterByTier(IReadOnlyList<FlfFont> fonts, string tier) =>
    tier == "all"
        ? [.. fonts]
        : [.. fonts.Where(f => string.Equals(f.Tier, tier, StringComparison.OrdinalIgnoreCase))];

static void DrawScreen(IReadOnlyList<FlfFont> fonts, ref int fontIndex, string word, string status, string tierFilter)
{
    Console.Clear();

    if (fonts.Count == 0)
    {
        Console.WriteLine($"No fonts in tier '{tierFilter}'. /tier all to reset.");
        return;
    }

    // Legend + active filter.
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"[v]=verified  [ ]=clean  [~]=approximate    filter: {tierFilter} (/tier all|verified|clean|approximate)");
    Console.ResetColor();

    // Font list panel: up to 15 names centered on the active selection.
    int listRows = Math.Min(15, fonts.Count);
    int start = Math.Clamp(fontIndex - listRows / 2, 0, Math.Max(0, fonts.Count - listRows));
    for (var i = start; i < start + listRows; i++)
    {
        bool active = i == fontIndex;
        var tag = fonts[i].Tier switch
        {
            "verified" => "[v]",
            "approximate" => "[~]",
            _ => "[ ]",
        };
        if (active)
        {
            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.White;
            Console.WriteLine($"> {tag} {fonts[i].Name}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  {tag} {fonts[i].Name}");
            Console.ResetColor();
        }
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Font [{fontIndex + 1}/{fonts.Count}]: {fonts[fontIndex].Name}  (height {fonts[fontIndex].Height}, tier {fonts[fontIndex].Tier})");
    Console.ResetColor();

    Console.Write($"Word: {word}");
    Console.CursorVisible = true;
    Console.WriteLine();
    Console.WriteLine(new string('─', Math.Min(Console.WindowWidth > 0 ? Console.WindowWidth : 80, 100)));

    if (word.Length > 0 && !word.StartsWith('/'))
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Renderer.RenderToString(fonts[fontIndex], word));
        Console.ResetColor();
    }

    if (status.Length > 0)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(status);
        Console.ResetColor();
    }
}

static string RunCommand(string command, IReadOnlyList<FlfFont> fonts, ref int fontIndex, ref string status)
{
    var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var verb = parts[0].ToLowerInvariant();
    var rest = parts.Length > 1 ? parts[1] : "";

    switch (verb)
    {
        case "/quit" or "/exit" or "/q":
            Console.Clear();
            Environment.Exit(0);
            return "";

        case "/fonts":
            PagedList(fonts.Select(f => f.Name).ToList());
            status = "Done.";
            return "";

        case "/all":
        {
            var target = rest.Length > 0 ? rest : "";
            if (target.Length == 0)
            {
                status = "Usage: /all <word>";
                return "";
            }
            foreach (var font in fonts)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"=== {font.Name} [{font.Tier}] ===");
                Console.ResetColor();
                Console.WriteLine(Renderer.RenderToString(font, target));
                Console.WriteLine();
                Console.WriteLine("any key = next font, q = stop");
                if (Console.ReadKey(intercept: true).KeyChar is 'q' or 'Q')
                    break;
            }
            status = "Done.";
            return target;
        }

        case "/font":
        {
            var match = fonts
                .Select((f, i) => (f, i))
                .FirstOrDefault(t => t.f.Name.Contains(rest, StringComparison.OrdinalIgnoreCase));
            if (match.f is null)
            {
                status = $"No font matching '{rest}'.";
            }
            else
            {
                fontIndex = match.i;
                status = $"Selected {match.f.Name}.";
            }
            return "";
        }

        case "/clear":
            return "";

        case "/help":
            status = "Keys: type=word, Bksp=del, Enter=render, arrows/PgUp/PgDn/Tab=font, Esc=quit. " +
                     "Cmds: /all <word>, /font <name>, /tier all|verified|clean|approximate, /fonts, /clear, /quit";
            return "";

        default:
            status = $"Unknown command '{verb}'. /help for commands.";
            return command;
    }
}

static void PagedList(IReadOnlyList<string> names)
{
    const int pageSize = 20;
    for (var i = 0; i < names.Count; i += pageSize)
    {
        Console.Clear();
        for (var j = i; j < Math.Min(i + pageSize, names.Count); j++)
            Console.WriteLine($"  {names[j]}");
        Console.WriteLine();
        Console.Write($"-- {Math.Min(i + pageSize, names.Count)}/{names.Count} -- any key = more, q = back ");
        if (Console.ReadKey(intercept: true).KeyChar is 'q' or 'Q')
            break;
    }
}
