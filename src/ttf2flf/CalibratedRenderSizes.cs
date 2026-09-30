namespace ttf2flf;

/// <summary>
/// PNG-calibrated render sizes for the bundled corpus fonts, keyed by font file name.
/// Generated from Corpus/render_sizes.json (each value reproduces the font author's
/// reference bitmap at 100% exact match, except the fallback-unverified set noted in the
/// source file). These are the authoritative render sizes for these fonts.
/// </summary>
public static class CalibratedRenderSizes
{
    public static readonly IReadOnlyDictionary<string, int> ByFileName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Absolute 10.ttf"] = 16,
            ["Ark 6.ttf"] = 16,
            ["Awesome 9.ttf"] = 16,
            ["Bacteria 12.ttf"] = 16,
            ["Brief 9.ttf"] = 16,
            ["Buzzer 11.ttf"] = 16,
            ["Candy 10.ttf"] = 17,
            ["Compass 9.ttf"] = 16,
            ["Corset 8.ttf"] = 16,
            ["Curse 12.ttf"] = 16,
            ["Dust 7 Clean.ttf"] = 16,
            ["Dust 7.ttf"] = 16,
            ["Empire 7.ttf"] = 16,
            ["Fear 11.ttf"] = 16,
            ["Futile 14.ttf"] = 32,
            ["Gecko 16 Turkish.ttf"] = 33,
            ["Gecko 16.ttf"] = 33,
            ["Holotype 9.ttf"] = 16,
            ["Hungry 7.ttf"] = 16,
            ["Ignore 17.ttf"] = 32,
            ["Jewel 6 Turkish.ttf"] = 17,
            ["Jewel 6.ttf"] = 17,
            ["Kobold 7.ttf"] = 16,
            ["Lookout 7.ttf"] = 16,
            ["Loser 7.ttf"] = 17,
            ["Manticore 14.ttf"] = 13,
            ["Mask 17.ttf"] = 32,
            ["Match 7.ttf"] = 16,
            ["Memo 9.ttf"] = 16,
            ["More 15.ttf"] = 32,
            ["Nocive 15.ttf"] = 16,
            ["Nope 8 Bold.ttf"] = 16,
            ["Nope 8.ttf"] = 17,
            ["Outflank 9.ttf"] = 16,
            ["Passage 7.ttf"] = 16,
            ["Quit 13.ttf"] = 16,
            ["Rude 11.ttf"] = 17,
            ["Saga 8.ttf"] = 16,
            ["Salty 9.ttf"] = 16,
            ["Sins 7.ttf"] = 17,
            ["Smart 9 Turkish.ttf"] = 16,
            ["Smart 9.ttf"] = 16,
            ["Tape 14.ttf"] = 16,
            ["Teatime 7.ttf"] = 16,
            ["Torch 6.ttf"] = 16,
            ["Troll 12.ttf"] = 16,
            ["Unfair 8.ttf"] = 16,
            ["Urban 20.ttf"] = 32,
            ["Vest 9.ttf"] = 17,
            ["Voice 9 Turkish.ttf"] = 16,
            ["Voice 9.ttf"] = 16,
            ["Winds 7.ttf"] = 16,
            ["Xerxes 10.ttf"] = 16,
            ["Yesterday 10.ttf"] = 16,
            ["Ziplock 13.ttf"] = 16,
        };
}
