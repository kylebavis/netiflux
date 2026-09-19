using Netiflux.Theming;

namespace Netiflux.Ui.Tests;

/// <summary>
/// Terminal.Gui silently ignores theme config it can't parse, so a format change shows up
/// as missing themes rather than an error. These pin the bundled themes actually loading.
/// </summary>
[Collection(nameof(UiTestCollection))]
public class ThemeCatalogTests
{
    private static readonly string[] BundledThemes =
    [
        "Netiflux Dark", "Netiflux Light", "Netiflux Gruvbox", "Netiflux Nord", "Netiflux Rose Pine"
    ];

    public ThemeCatalogTests() => ThemeCatalog.Initialize("Netiflux Dark");

    [Fact]
    public void Initialize_LoadsEveryBundledTheme()
    {
        var names = ThemeCatalog.GetThemeNames();

        Assert.All(BundledThemes, theme => Assert.Contains(theme, names));
        Assert.Equal("Netiflux Dark", ThemeCatalog.CurrentTheme);
    }

    [Theory]
    [InlineData("Sidebar")]
    [InlineData("EntryUnread")]
    public void Resolve_ReturnsTheThemesOwnScheme_NotTheBaseFallback(string schemeName)
    {
        var resolved = ThemeCatalog.Resolve(schemeName);
        var fallback = ThemeCatalog.Resolve("NoSuchScheme");

        Assert.NotEqual(fallback.Normal, resolved.Normal);
    }

    [Fact]
    public void Apply_SwitchesTheme_AndResolvesItsSchemes()
    {
        var dark = ThemeCatalog.Resolve("Sidebar").Normal;

        ThemeCatalog.Apply("Netiflux Light");
        var light = ThemeCatalog.Resolve("Sidebar").Normal;
        ThemeCatalog.Apply("Netiflux Dark");

        Assert.Equal("Netiflux Dark", ThemeCatalog.CurrentTheme);
        Assert.NotEqual(dark, light);
    }
}
