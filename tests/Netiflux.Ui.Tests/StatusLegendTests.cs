using Terminal.Gui.Input;

namespace Netiflux.Ui.Tests;

/// <summary>
/// The status legend is the only guidance that stays on screen — the welcome text in the
/// reader is replaced by the first article — so it has to keep working.
/// </summary>
[Collection(nameof(UiTestCollection))]
public class StatusLegendTests
{
    [Fact]
    public async Task Legend_AlwaysOffersHelpAndQuit()
    {
        await using var ui = await ShellHarness.StartAsync();

        var legend = await ui.ReadAsync(() => ui.Shell.Status.Legend);

        Assert.Contains("? help", legend, StringComparison.Ordinal);
        Assert.Contains("q quit", legend, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legend_MentionsTheThemePickerOnAWideTerminal()
    {
        // Ctrl+T is otherwise undiscoverable without opening the help dialog.
        await using var ui = await ShellHarness.StartAsync();

        var legend = await ui.ReadAsync(() => ui.Shell.Status.Legend);

        Assert.Contains("Ctrl+T", legend, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legend_FitsTheTerminalWidth()
    {
        // A legend longer than the window gets chopped mid-word, which is how "q quit"
        // would silently disappear again.
        await using var ui = await ShellHarness.StartAsync();

        var legend = await ui.ReadAsync(() => ui.Shell.Status.Legend);

        Assert.True(
            legend.Length <= ShellHarness.ScreenWidth,
            $"legend is {legend.Length} chars but the screen is {ShellHarness.ScreenWidth}");
    }

    [Fact]
    public async Task Legend_ChangesWhileSearching()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync('/');
        await ui.WaitForUiAsync(() => ui.Shell.SearchBarVisible, "the search bar to open");

        var legend = await ui.ReadAsync(() => ui.Shell.Status.Legend);

        Assert.Contains("Esc cancel", legend, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Legend_ExplainsHowToLeaveTheReader()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync('z');
        await ui.WaitForUiAsync(() => ui.Shell.PaneLayout == PaneMode.ReaderOnly, "zen mode");

        var legend = await ui.ReadAsync(() => ui.Shell.Status.Legend);

        Assert.Contains("Esc back", legend, StringComparison.Ordinal);
        Assert.Contains("q quit", legend, StringComparison.Ordinal);
    }
}
