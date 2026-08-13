using Netiflux.Core;
using Netiflux.Core.Configuration;
using Netiflux.Core.Models;
using Terminal.Gui.Input;

namespace Netiflux.Ui.Tests;

/// <summary>
/// The triage key map, exercised against a running shell. These are the bindings whose
/// breakage is invisible to a unit test: every bug found in this area so far was a focus
/// or routing problem, not a logic problem.
/// </summary>
[Collection(nameof(UiTestCollection))]
public class TriageKeyTests
{
    [Fact]
    public async Task Save_AppliesToTheHighlightedRow_WithoutOpeningIt()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.J);
        await ui.PressAsync('s');

        await ui.WaitForAsync(() => ui.Client.Saved.Contains(2), "entry 2 to be saved");
        Assert.Equal([2], ui.Client.Saved);
    }

    [Fact]
    public async Task Save_RecordsTheEntryInTheLocalSavedStore()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync('s');

        await ui.WaitForAsync(() => ui.Session.SavedStore.IsSaved(1), "entry 1 to be recorded locally");
    }

    [Fact]
    public async Task Star_AppliesToTheHighlightedRow()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.J);
        await ui.PressAsync('f');

        await ui.WaitForAsync(() => ui.Client.Bookmarked.Contains(2), "entry 2 to be starred");
    }

    [Fact]
    public async Task MovingTheCursor_MarksTheEntryRead()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.J);

        await ui.WaitForAsync(
            () => ui.Client.Entries[1].Status == EntryStatus.Read,
            "entry 2 to be marked read on selection");
    }

    [Fact]
    public async Task TheInitialSelection_IsNotMarkedRead()
    {
        // Opening the app must not consume the article sitting under the cursor.
        await using var ui = await ShellHarness.StartAsync();

        Assert.Equal(EntryStatus.Unread, ui.Client.Entries[0].Status);
    }

    [Fact]
    public async Task MarkOnSelect_CanBeTurnedOff()
    {
        await using var ui = await ShellHarness.StartAsync(
            config: new NetifluxConfig { PageSize = 50, AutoMarkRead = AutoMarkRead.Never });

        await ui.PressAsync(Key.J);
        await ui.SettleAsync();

        Assert.Equal(EntryStatus.Unread, ui.Client.Entries[1].Status);
    }

    [Fact]
    public async Task Space_MarksRowsForABatchAndSaveAppliesToAllOfThem()
    {
        await using var ui = await ShellHarness.StartAsync();

        // Space marks the row and advances, so two presses select two entries.
        await ui.PressAsync(Key.Space);
        await ui.PressAsync(Key.Space);
        await ui.PressAsync('s');

        await ui.WaitForAsync(() => ui.Client.Saved.Count == 2, "both marked entries to be saved");
        Assert.Equal([1, 2], ui.Client.Saved.Order());
    }

    [Fact]
    public async Task AutoMarkRead_DoesNotWipeTheBatchSelection()
    {
        // Regression: marking read on cursor move used to clear the marked set, so the
        // batch was silently reduced to whatever row the cursor happened to be on.
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.Space);
        await ui.PressAsync(Key.Space);

        await ui.WaitForUiAsync(() => ui.Shell.HasMarkedEntries, "the batch selection to survive");
    }

    [Fact]
    public async Task Escape_ClearsTheBatchSelection()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.Space);
        await ui.WaitForUiAsync(() => ui.Shell.HasMarkedEntries, "a row to be marked");

        await ui.PressAsync(Key.Esc);
        await ui.WaitForUiAsync(() => !ui.Shell.HasMarkedEntries, "the selection to clear");
    }

    [Fact]
    public async Task ToggleRead_FlipsTheHighlightedEntryBack()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.J);
        await ui.WaitForAsync(() => ui.Client.Entries[1].Status == EntryStatus.Read, "auto mark-read");

        await ui.PressAsync('m');
        await ui.WaitForAsync(() => ui.Client.Entries[1].Status == EntryStatus.Unread, "toggle back to unread");
    }

    [Fact]
    public async Task RefreshFeeds_AsksTheServerToPoll()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync(Key.R.WithShift);

        await ui.WaitForAsync(() => ui.Client.RefreshCount == 1, "a server-side refresh");
    }

    [Fact]
    public async Task ZenMode_TogglesTheReaderToFullWidthAndBack()
    {
        await using var ui = await ShellHarness.StartAsync();

        await ui.PressAsync('z');
        await ui.WaitForUiAsync(() => ui.Shell.PaneLayout == PaneMode.ReaderOnly, "zen mode");

        await ui.PressAsync('z');
        await ui.WaitForUiAsync(() => ui.Shell.PaneLayout == PaneMode.Split, "the split layout to return");
    }

    [Fact]
    public async Task SaveFailure_LeavesTheEntryUnsaved()
    {
        await using var ui = await ShellHarness.StartAsync();

        ui.Client.NextFailure = new MinifluxException(
            "No third-party integration is configured", System.Net.HttpStatusCode.BadRequest);

        await ui.PressAsync('s');
        await ui.SettleAsync();

        Assert.Empty(ui.Client.Saved);
        Assert.False(ui.Session.SavedStore.IsSaved(1));
    }
}

[CollectionDefinition(nameof(UiTestCollection), DisableParallelization = true)]
public sealed class UiTestCollection;
