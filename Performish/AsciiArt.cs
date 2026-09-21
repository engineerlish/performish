using System.Drawing;
using System.Linq;

namespace Performish
{
    /// <summary>The banner art, kept in its own file/constant (not inlined in UI code) so it can be
    /// replaced without touching rendering logic - see DECISIONS.md "banner is data, not code".
    /// Plain ASCII only (letters, digits, standard punctuation) per CONVENTIONS.md - Consolas has no
    /// glyph for Unicode block-drawing characters, and this app renders through a WinForms
    /// RichTextBox (not an actual console host), so the only encoding concern that matters is "is
    /// this valid in the source file's UTF-8 without BOM surprises", which plain ASCII always is.</summary>
    public static class AsciiArt
    {
        /// <summary>Exact banner supplied for the Performish rebrand - preserved verbatim (spacing,
        /// characters, line breaks). Do not reflow or trim trailing spaces; they are load-bearing for
        /// the letterforms' alignment under a monospace font.</summary>
        public static readonly string[] Logo =
        {
            "                           .d888                               d8b          888     ",
            "                          d88P\"                                Y8P          888     ",
            "                          888                                               888     ",
            "88888b.   .d88b.  888d888 888888 .d88b.  888d888 88888b.d88b.  888 .d8888b  88888b. ",
            "888 \"88b d8P  Y8b 888P\"   888   d88\"\"88b 888P\"   888 \"888 \"88b 888 88K      888 \"88b",
            "888  888 88888888 888     888   888  888 888     888  888  888 888 \"Y8888b. 888  888",
            "888 d88P Y8b.     888     888   Y88..88P 888     888  888  888 888      X88 888  888",
            "88888P\"   \"Y8888  888     888    \"Y88P\"  888     888  888  888 888  88888P' 888  888",
            "888                                                                                 ",
            "888                                                                                 ",
            "888                                                                                 ",
        };

        /// <summary>Compact single-line fallback for when the window/console is too narrow to show
        /// Logo without a horizontal scrollbar - see ChooseBanner(). Kept in plain ASCII too.</summary>
        public static readonly string[] CompactLogo = { "=== PERFORMISH ===" };

        public static int LogoWidthChars => Logo.Max(line => line.Length);

        /// <summary>Picks the full banner or the compact fallback based on how much horizontal room
        /// is actually available, measured in real rendered pixels for the given font (a plain
        /// character-count comparison would be wrong the moment the font or DPI changes).</summary>
        public static string[] ChooseBanner(Graphics g, Font font, int availableWidthPx)
        {
            var widestLine = Logo.OrderByDescending(l => l.Length).First();
            var neededWidthPx = g.MeasureString(widestLine, font).Width;
            return neededWidthPx <= availableWidthPx ? Logo : CompactLogo;
        }
    }
}
