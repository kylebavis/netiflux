using Netiflux.Core.Models;
using Netiflux.Core.State;
using Netiflux.Ui;

namespace Netiflux.Core.Tests;

public class UiSourceTests
{
    // ---------------------------------------------------------- sidebar

    [Fact]
    public void Sidebar_PutsSavedViewsFirstAndGroupsTheRest()
    {
        var source = NewSidebar();

        var labels = Enumerable.Range(0, source.Count).Select(i => source[i]!.Label).ToList();

        Assert.Equal("VIEWS", labels[0]);
        Assert.Contains("Unread", labels);
        Assert.Contains("Starred", labels);
        Assert.Contains("CATEGORIES", labels);
        Assert.Contains("FEEDS", labels);
    }

    [Fact]
    public void Sidebar_HidesTheCategorySectionWhenThereIsOnlyOne()
    {
        // A lone category duplicates "Unread" and only costs vertical space.
        var source = new SidebarSource(GlyphSet.Ascii);
        source.Rebuild(
            [new Category { Id = 1, Title = "Everything", TotalUnread = 6 }],
            [new Feed { Id = 1, Title = "Alpha" }],
            unreadTotal: 6,
            starredTotal: 0);

        var labels = AllLabels(source);

        Assert.DoesNotContain("CATEGORIES", labels);
        Assert.Contains("Unread", labels);
    }

    [Fact]
    public void Sidebar_WithManyFeeds_ShowsOnlyThoseWithUnreadPlusAToggle()
    {
        // Mirrors a real subscription list: hundreds of feeds, a handful active.
        var feeds = Enumerable.Range(1, 349)
            .Select(i => new Feed { Id = i, Title = $"Feed {i:D3}" })
            .ToList();

        var unread = new Dictionary<long, int> { [5] = 3, [12] = 2, [200] = 1 };

        var source = new SidebarSource(GlyphSet.Ascii);
        source.Rebuild([new Category { Id = 1, Title = "All", TotalUnread = 6 }], feeds, 6, 0, unread);

        var feedRows = RowsOfKind(source, SidebarKind.Feed);

        Assert.Equal(3, feedRows.Count);
        Assert.Equal(["Feed 005", "Feed 012", "Feed 200"], feedRows.Select(f => f.Label));
        Assert.Equal([3, 2, 1], feedRows.Select(f => f.Count));

        // The whole rail stays short enough to take in at a glance.
        Assert.True(source.Count < 15, $"sidebar had {source.Count} rows");

        var toggle = Assert.Single(RowsOfKind(source, SidebarKind.ToggleFeeds));
        Assert.Contains("346", toggle.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_ToggleExpandsToEveryFeedAndBack()
    {
        var feeds = Enumerable.Range(1, 50).Select(i => new Feed { Id = i, Title = $"Feed {i:D2}" }).ToList();
        var unread = new Dictionary<long, int> { [7] = 4 };

        var source = new SidebarSource(GlyphSet.Ascii);
        source.Rebuild([], feeds, 4, 0, unread);

        Assert.Single(RowsOfKind(source, SidebarKind.Feed));

        source.ToggleShowAllFeeds();
        source.Rebuild([], feeds, 4, 0, unread);

        Assert.Equal(50, RowsOfKind(source, SidebarKind.Feed).Count);
        Assert.Equal("Feed 07", RowsOfKind(source, SidebarKind.Feed)[0].Label);
        Assert.Contains("only unread", Assert.Single(RowsOfKind(source, SidebarKind.ToggleFeeds)).Label);

        source.ToggleShowAllFeeds();
        source.Rebuild([], feeds, 4, 0, unread);

        Assert.Single(RowsOfKind(source, SidebarKind.Feed));
    }

    [Fact]
    public void Sidebar_AlwaysShowsBrokenFeedsEvenWithNoUnread()
    {
        // A feed that has stopped working looks exactly like a quiet one otherwise.
        var feeds = new List<Feed>
        {
            new() { Id = 1, Title = "Quiet" },
            new() { Id = 2, Title = "Broken", ParsingErrorCount = 53 }
        };

        var source = new SidebarSource(GlyphSet.Ascii);
        source.Rebuild([], feeds, 0, 0, new Dictionary<long, int>());

        var feedRows = RowsOfKind(source, SidebarKind.Feed);

        Assert.Single(feedRows);
        Assert.StartsWith("Broken", feedRows[0].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_WithNoUnreadAnywhere_OffersToShowEverything()
    {
        var feeds = Enumerable.Range(1, 20).Select(i => new Feed { Id = i, Title = $"F{i}" }).ToList();

        var source = new SidebarSource(GlyphSet.Ascii);
        source.Rebuild([], feeds, 0, 0, new Dictionary<long, int>());

        var toggle = Assert.Single(RowsOfKind(source, SidebarKind.ToggleFeeds));
        Assert.Contains("all 20", toggle.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_ToggleRowIsInertSoItNeverLoadsEntries()
    {
        var source = new SidebarSource(GlyphSet.Ascii);
        source.Rebuild([], [new Feed { Id = 1, Title = "A" }], 0, 0, new Dictionary<long, int>());

        var toggle = Assert.Single(RowsOfKind(source, SidebarKind.ToggleFeeds));

        Assert.True(toggle.IsInert);
        Assert.False(toggle.IsHeader);
    }

    [Fact]
    public void Sidebar_OrdersCategoriesByUnreadCountFirst()
    {
        var source = NewSidebar();

        var categories = Enumerable.Range(0, source.Count)
            .Select(i => source[i]!)
            .SkipWhile(i => i.Label != "CATEGORIES")
            .Skip(1)
            .TakeWhile(i => i.Kind == SidebarKind.Category)
            .ToList();

        Assert.Equal(["News", "Tech", "Quiet"], categories.Select(c => c.Label));
    }

    [Fact]
    public void Sidebar_NextSelectable_SkipsHeadersInBothDirections()
    {
        var source = NewSidebar();

        var headerIndex = FirstIndexOf(source, "CATEGORIES");

        // Arriving downward lands past the header, not on it.
        var down = source.NextSelectable(headerIndex, 1);
        Assert.False(source[down]!.IsHeader);
        Assert.True(down > headerIndex);

        // Arriving upward lands before it.
        var up = source.NextSelectable(headerIndex, -1);
        Assert.False(source[up]!.IsHeader);
        Assert.True(up < headerIndex);
    }

    [Fact]
    public void Sidebar_FlagsFeedsWithParsingErrors()
    {
        var source = NewSidebar();

        var broken = Enumerable.Range(0, source.Count)
            .Select(i => source[i]!)
            .First(i => i.Kind == SidebarKind.Feed && i.Label.StartsWith("Broken", StringComparison.Ordinal));

        Assert.EndsWith("!", broken.Label, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SidebarKind.Unread)]
    [InlineData(SidebarKind.Starred)]
    [InlineData(SidebarKind.All)]
    [InlineData(SidebarKind.Today)]
    public void SidebarItem_BuildsAQueryForEachSavedView(SidebarKind kind)
    {
        var query = new SidebarItem(kind, "x").ToQuery(50);

        Assert.Equal(50, query.Limit);

        switch (kind)
        {
            case SidebarKind.Unread:
                Assert.Equal([EntryStatus.Unread], query.Statuses);
                break;
            case SidebarKind.Starred:
                Assert.True(query.Starred);
                break;
            case SidebarKind.Today:
                Assert.NotNull(query.PublishedAfter);
                break;
            case SidebarKind.All:
                Assert.Null(query.Starred);
                Assert.Contains(EntryStatus.Read, query.Statuses);
                break;
        }
    }

    [Fact]
    public void SidebarItem_ScopesCategoryAndFeedQueriesById()
    {
        Assert.Equal(7, new SidebarItem(SidebarKind.Category, "Tech", null, 7).ToQuery(10).CategoryId);
        Assert.Equal(9, new SidebarItem(SidebarKind.Feed, "Feed", null, 9).ToQuery(10).FeedId);
    }

    // ------------------------------------------------------- entry list

    [Fact]
    public void EntryList_MarksTrackTheRowsTheyWereSetOn()
    {
        var source = NewEntryList(5);

        source.SetMark(1, true);
        source.SetMark(3, true);

        Assert.True(source.HasMarks);
        Assert.Equal([2, 4], source.MarkedEntries.Select(e => e.Id));
    }

    [Fact]
    public void EntryList_RemoveAt_ShiftsMarksOnLaterRowsDown()
    {
        var source = NewEntryList(5);

        source.SetMark(3, true);
        source.RemoveAt(1);

        // The entry that was at index 3 is now at index 2 and must still be marked.
        Assert.Equal([4], source.MarkedEntries.Select(e => e.Id));
        Assert.True(source.IsMarked(2));
        Assert.Equal(4, source.Count);
    }

    [Fact]
    public void EntryList_RemoveAt_DropsAMarkOnTheRemovedRow()
    {
        var source = NewEntryList(3);

        source.SetMark(1, true);
        source.RemoveAt(1);

        Assert.False(source.HasMarks);
    }

    [Fact]
    public void EntryList_SetEntries_ClearsStaleMarks()
    {
        var source = NewEntryList(3);
        source.SetMark(0, true);

        source.SetEntries(Entries(2));

        Assert.False(source.HasMarks);
        Assert.Equal(2, source.Count);
    }

    [Fact]
    public void EntryList_RemoveAt_IgnoresOutOfRangeIndices()
    {
        var source = NewEntryList(2);

        source.RemoveAt(-1);
        source.RemoveAt(99);

        Assert.Equal(2, source.Count);
    }

    [Theory]
    [InlineData(30, "30m")]
    [InlineData(60 * 5, "5h")]
    [InlineData(60 * 24 * 3, "3d")]
    [InlineData(60 * 24 * 14, "2w")]
    [InlineData(60 * 24 * 400, "1y")]
    public void FormatAge_IsCompactAndUnitAppropriate(int minutesAgo, string expected)
    {
        var published = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);

        Assert.Equal(expected, EntryListSource.FormatAge(published));
    }

    [Fact]
    public void FormatAge_TreatsFuturePublishDatesAsJustNow()
    {
        // Feeds with bad clocks are common; this must not render a negative age.
        Assert.Equal("1m", EntryListSource.FormatAge(DateTimeOffset.UtcNow.AddHours(2)));
    }

    // --------------------------------------------------------- glyphs

    [Fact]
    public void GlyphSets_DefineADistinctMarkerForEveryState()
    {
        foreach (var set in new[] { GlyphSet.Unicode, GlyphSet.Ascii })
        {
            var markers = new[] { set.Unread, set.Read, set.Starred, set.Saved, set.SavedAndStarred, set.Selected };
            Assert.Equal(markers.Length, markers.Distinct().Count());
        }
    }

    // --------------------------------------------------------- helpers

    private static int FirstIndexOf(SidebarSource source, string label) =>
        Enumerable.Range(0, source.Count).First(i => source[i]!.Label == label);

    private static List<string> AllLabels(SidebarSource source) =>
        Enumerable.Range(0, source.Count).Select(i => source[i]!.Label).ToList();

    private static List<SidebarItem> RowsOfKind(SidebarSource source, SidebarKind kind) =>
        Enumerable.Range(0, source.Count).Select(i => source[i]!).Where(i => i.Kind == kind).ToList();

    private static SidebarSource NewSidebar()
    {
        var source = new SidebarSource(GlyphSet.Ascii);

        source.Rebuild(
            [
                new Category { Id = 1, Title = "Tech", TotalUnread = 12 },
                new Category { Id = 2, Title = "News", TotalUnread = 30 },
                new Category { Id = 3, Title = "Quiet", TotalUnread = 0 }
            ],
            [
                new Feed { Id = 1, Title = "Alpha" },
                new Feed { Id = 2, Title = "Broken Feed", ParsingErrorCount = 3 }
            ],
            unreadTotal: 42,
            starredTotal: 5);

        return source;
    }

    private static EntryListSource NewEntryList(int count)
    {
        var store = SavedEntryStore.Load(Path.Combine(
            Path.GetTempPath(), "netiflux-tests", Guid.NewGuid().ToString("N"), "saved.json"));

        var source = new EntryListSource(store, GlyphSet.Ascii);
        source.SetEntries(Entries(count));
        return source;
    }

    private static List<Entry> Entries(int count) =>
        Enumerable.Range(1, count).Select(i => new Entry
        {
            Id = i,
            Title = $"Entry {i}",
            Status = EntryStatus.Unread,
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-i),
            Feed = new Feed { Id = 1, Title = "Feed" }
        }).ToList();
}
