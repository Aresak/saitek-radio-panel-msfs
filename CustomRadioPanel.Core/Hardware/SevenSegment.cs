namespace CustomRadioPanel.Core.Hardware;

/// <summary>
/// Encodes text into the byte values understood by the Saitek/Logitech radio panel's
/// 5-digit 7-segment displays and assembles the 22-byte display buffer.
///
/// Per-digit encoding (verified against bjanders/fpanels radiopanel.go):
///   0x00-0x09  digit 0-9
///   0xD0-0xD9  digit 0-9 with decimal point
///   0xEF       dash '-'
///   0x0F       blank
/// </summary>
public static class SevenSegment
{
    public const int DigitsPerDisplay = 5;
    public const int DisplayCount = 4;        // top-left, top-right, bottom-left, bottom-right
    public const int BufferLength = 22;       // 4 * 5 digit bytes + 2 trailing padding bytes

    public const byte Blank = 0x0F;
    public const byte Dash = 0xEF;
    private const byte DotBase = 0xD0;        // 0xD0 + digit => digit with dot

    /// <summary>
    /// Encodes a single display's text into exactly 5 bytes, right-aligned and padded with blanks.
    /// Accepts digits, '.', '-' and spaces. A '.' attaches a decimal point to the preceding digit
    /// (it does not consume a cell). Unsupported characters render as blank.
    /// </summary>
    public static byte[] EncodeDisplay(string? text)
    {
        var glyphs = new List<byte>(DigitsPerDisplay + 2);
        if (!string.IsNullOrEmpty(text))
        {
            foreach (char c in text)
            {
                switch (c)
                {
                    case '.':
                        // Attach a dot to the previous plain digit; if none, emit "0." as a placeholder.
                        if (glyphs.Count > 0 && glyphs[^1] <= 0x09)
                            glyphs[^1] = (byte)(DotBase + glyphs[^1]);
                        else
                            glyphs.Add(DotBase);
                        break;
                    case '-':
                        glyphs.Add(Dash);
                        break;
                    case ' ':
                        glyphs.Add(Blank);
                        break;
                    default:
                        glyphs.Add(c is >= '0' and <= '9' ? (byte)(c - '0') : Blank);
                        break;
                }
            }
        }

        var cells = new byte[DigitsPerDisplay];
        for (int i = 0; i < DigitsPerDisplay; i++)
            cells[i] = Blank;

        // Right-align: keep the last 5 glyphs if there are more than fit.
        int start = Math.Max(0, glyphs.Count - DigitsPerDisplay);
        int count = Math.Min(DigitsPerDisplay, glyphs.Count);
        int offset = DigitsPerDisplay - count;
        for (int i = 0; i < count; i++)
            cells[offset + i] = glyphs[start + i];

        return cells;
    }

    /// <summary>
    /// Builds the full 22-byte display buffer from the four display strings.
    /// Order matches the hardware: top-left (active/upper), top-right (standby/upper),
    /// bottom-left (active/lower), bottom-right (standby/lower).
    /// </summary>
    public static byte[] BuildBuffer(string? topLeft, string? topRight, string? bottomLeft, string? bottomRight)
    {
        var buffer = new byte[BufferLength];
        Array.Copy(EncodeDisplay(topLeft), 0, buffer, 0, DigitsPerDisplay);
        Array.Copy(EncodeDisplay(topRight), 0, buffer, 5, DigitsPerDisplay);
        Array.Copy(EncodeDisplay(bottomLeft), 0, buffer, 10, DigitsPerDisplay);
        Array.Copy(EncodeDisplay(bottomRight), 0, buffer, 15, DigitsPerDisplay);
        buffer[20] = 0x00;
        buffer[21] = 0x00;
        return buffer;
    }

    /// <summary>A buffer that switches every segment off.</summary>
    public static byte[] AllOff()
    {
        var buffer = new byte[BufferLength];
        for (int i = 0; i < BufferLength; i++)
            buffer[i] = 0xFF;
        return buffer;
    }
}
