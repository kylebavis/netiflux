namespace Netiflux.Ui;

/// <summary>
/// The small marker characters the entry list draws. Windows consoles other than
/// Windows Terminal still ship raster fonts that render box-drawing and symbol
/// codepoints as blanks, so every glyph has an ASCII stand-in.
/// </summary>
public sealed record GlyphSet(
    string Unread,
    string Read,
    string Starred,
    string Saved,
    string SavedAndStarred,
    string Selected,
    string Bullet)
{
    public static readonly GlyphSet Unicode = new(
        Unread: "●",
        Read: "○",
        Starred: "★",
        Saved: "⤓",
        SavedAndStarred: "✦",
        Selected: "▸",
        Bullet: "·");

    public static readonly GlyphSet Ascii = new(
        Unread: "*",
        Read: "-",
        Starred: "+",
        Saved: "v",
        SavedAndStarred: "#",
        Selected: ">",
        Bullet: ".");

    /// <summary>
    /// Picks a set for the current terminal. Windows Terminal, VS Code and any UTF-8
    /// capable *nix terminal get the nicer glyphs; legacy conhost falls back to ASCII.
    /// </summary>
    public static GlyphSet Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Unicode;
        }

        // WT_SESSION is set by Windows Terminal; TERM_PROGRAM covers VS Code's terminal.
        var isModern =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERM_PROGRAM"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConEmuANSI"));

        return isModern ? Unicode : Ascii;
    }
}
