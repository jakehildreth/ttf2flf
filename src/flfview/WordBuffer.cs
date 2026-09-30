using System.Text;

namespace flfview;

/// <summary>
/// The interactive word being typed. Console input delivers a supplementary character
/// as two UTF-16 surrogate key events; the buffer holds a high surrogate until its low
/// surrogate arrives, so <see cref="Text"/> never contains an unpaired surrogate.
/// Backspace removes one complete Unicode scalar value.
/// </summary>
public sealed class WordBuffer
{
    private char? pendingHighSurrogate;
    private string text = "";

    public string Text
    {
        get => text;
        set
        {
            text = value;
            pendingHighSurrogate = null;
        }
    }

    /// <summary>Appends one typed UTF-16 unit; control characters and lone surrogates are ignored.</summary>
    public void Append(char c)
    {
        if (char.IsHighSurrogate(c))
        {
            pendingHighSurrogate = c;
            return;
        }

        if (char.IsLowSurrogate(c))
        {
            if (pendingHighSurrogate is { } high)
                text += new string([high, c]);
            pendingHighSurrogate = null;
            return;
        }

        pendingHighSurrogate = null;
        if (!char.IsControl(c))
            text += c;
    }

    /// <summary>Removes the last Unicode scalar value, if any.</summary>
    public void Backspace()
    {
        pendingHighSurrogate = null;
        if (text.Length == 0)
            return;

        // Consumes 2 units for a surrogate pair, 1 otherwise (including ill-formed data).
        Rune.DecodeLastFromUtf16(text, out _, out var consumed);
        text = text[..^consumed];
    }
}
