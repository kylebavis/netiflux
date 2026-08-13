using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Netiflux.Core.Models;
using Netiflux.Theming;
using Terminal.Gui.Views;

namespace Netiflux.Ui;

public enum SidebarKind
{
    Header,
    Unread,
    Today,
    Starred,
    All,
    Category,
    Feed,

    /// <summary>Expands or collapses the full feed list. Activated, not navigated into.</summary>
    ToggleFeeds
}

/// <summary>One row of the navigation rail. Headers are inert and get skipped by the cursor.</summary>
public sealed record SidebarItem(SidebarKind Kind, string Label, int? Count = null, long? Id = null)
{
    public bool IsHeader => Kind == SidebarKind.Header;

    /// <summary>True for rows that are not a view — they must not trigger an entry load.</summary>
    public bool IsInert => Kind is SidebarKind.Header or SidebarKind.ToggleFeeds;

    /// <summary>Builds the query this item scopes the entry list to.</summary>
    public EntryQuery ToQuery(int pageSize) => Kind switch
    {
        SidebarKind.Unread => new EntryQuery
        {
            Statuses = [EntryStatus.Unread],
            Limit = pageSize
        },
        SidebarKind.Today => new EntryQuery
        {
            Statuses = [EntryStatus.Unread, EntryStatus.Read],
            PublishedAfter = DateTimeOffset.UtcNow.AddHours(-24),
            Limit = pageSize
        },
        SidebarKind.Starred => new EntryQuery
        {
            Starred = true,
            Limit = pageSize
        },
        SidebarKind.Category => new EntryQuery
        {
            Statuses = [EntryStatus.Unread],
            CategoryId = Id,
            Limit = pageSize
        },
        SidebarKind.Feed => new EntryQuery
        {
            Statuses = [EntryStatus.Unread, EntryStatus.Read],
            FeedId = Id,
            Limit = pageSize
        },
        // SidebarKind.All and anything unexpected.
        _ => new EntryQuery
        {
            Statuses = [EntryStatus.Unread, EntryStatus.Read],
            Limit = pageSize
        }
    };
}

/// <summary>Renders the sidebar: saved views, then categories, then feeds, each with unread counts.</summary>
public sealed class SidebarSource : IListDataSource
{
    private readonly List<SidebarItem> _items = [];
    private GlyphSet _glyphs;
    private int _lastRenderWidth = 24;

    public SidebarSource(GlyphSet glyphs)
    {
        _glyphs = glyphs;
        Rebuild([], [], 0, 0);
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _items.Count;

    public int MaxItemLength => _lastRenderWidth;

    public bool SuspendCollectionChangedEvent { get; set; }

    public GlyphSet Glyphs
    {
        get => _glyphs;
        set => _glyphs = value;
    }

    public SidebarItem? this[int index] =>
        index >= 0 && index < _items.Count ? _items[index] : null;

    /// <summary>Whether the feed list shows every feed or only those with unread entries.</summary>
    public bool ShowAllFeeds { get; private set; }

    public void ToggleShowAllFeeds() => ShowAllFeeds = !ShowAllFeeds;

    public void Rebuild(
        IReadOnlyList<Category> categories,
        IReadOnlyList<Feed> feeds,
        int unreadTotal,
        int starredTotal,
        IReadOnlyDictionary<long, int>? feedUnread = null)
    {
        _items.Clear();
        feedUnread ??= new Dictionary<long, int>();

        _items.Add(new SidebarItem(SidebarKind.Header, "VIEWS"));
        _items.Add(new SidebarItem(SidebarKind.Unread, "Unread", unreadTotal));
        _items.Add(new SidebarItem(SidebarKind.Today, "Today"));
        _items.Add(new SidebarItem(SidebarKind.Starred, "Starred", starredTotal > 0 ? starredTotal : null));
        _items.Add(new SidebarItem(SidebarKind.All, "All"));

        // A single category carries no information — "Unread" already covers it — so the
        // section only earns its space once there are at least two to choose between.
        if (categories.Count > 1)
        {
            _items.Add(new SidebarItem(SidebarKind.Header, "CATEGORIES"));

            var byUnread = categories
                .OrderByDescending(c => c.TotalUnread)
                .ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase);

            foreach (var category in byUnread)
            {
                _items.Add(new SidebarItem(
                    SidebarKind.Category,
                    category.Title,
                    category.TotalUnread > 0 ? category.TotalUnread : null,
                    category.Id));
            }
        }

        AddFeedSection(feeds, feedUnread);

        if (!SuspendCollectionChangedEvent)
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    /// <summary>
    /// Lists feeds worth looking at. A large subscription list is mostly quiet at any
    /// given moment, and scrolling hundreds of silent feeds to reach the handful with new
    /// items is the opposite of triage — so unread feeds lead, and the rest stay behind a
    /// toggle. Broken feeds are always shown, because a feed that has stopped working
    /// looks identical to one that simply has no news.
    /// </summary>
    private void AddFeedSection(IReadOnlyList<Feed> feeds, IReadOnlyDictionary<long, int> feedUnread)
    {
        if (feeds.Count == 0)
        {
            return;
        }

        static int UnreadOf(IReadOnlyDictionary<long, int> counts, Feed feed) =>
            counts.TryGetValue(feed.Id, out var n) ? n : 0;

        var interesting = feeds
            .Where(f => UnreadOf(feedUnread, f) > 0 || f.ParsingErrorCount > 0)
            .OrderByDescending(f => UnreadOf(feedUnread, f))
            .ThenBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var visible = ShowAllFeeds
            ? interesting.Concat(
                feeds.Except(interesting).OrderBy(f => f.Title, StringComparer.CurrentCultureIgnoreCase)).ToList()
            : interesting;

        _items.Add(new SidebarItem(SidebarKind.Header, "FEEDS"));

        foreach (var feed in visible)
        {
            // Surface broken feeds rather than letting them silently stop updating.
            var label = feed.ParsingErrorCount > 0 ? feed.Title + " !" : feed.Title;
            var unread = UnreadOf(feedUnread, feed);

            _items.Add(new SidebarItem(SidebarKind.Feed, label, unread > 0 ? unread : null, feed.Id));
        }

        var hidden = feeds.Count - visible.Count;

        if (ShowAllFeeds)
        {
            _items.Add(new SidebarItem(SidebarKind.ToggleFeeds, "show only unread"));
        }
        else if (hidden > 0)
        {
            var label = visible.Count == 0
                ? $"show all {feeds.Count} feeds"
                : $"show {hidden} more…";

            _items.Add(new SidebarItem(SidebarKind.ToggleFeeds, label));
        }
    }

    /// <summary>
    /// Finds the next selectable row in <paramref name="direction"/>, so arrowing through
    /// the rail never parks the cursor on a section header.
    /// </summary>
    public int NextSelectable(int from, int direction)
    {
        var index = from;
        while (index >= 0 && index < _items.Count && _items[index].IsHeader)
        {
            index += direction;
        }

        if (index < 0 || index >= _items.Count)
        {
            // Ran off the end: search back the other way for something usable.
            index = from;
            while (index >= 0 && index < _items.Count && _items[index].IsHeader)
            {
                index -= direction;
            }
        }

        return Math.Clamp(index, 0, Math.Max(0, _items.Count - 1));
    }

    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value) { }

    public IList ToList() => _items;

    /// <summary>Nothing unmanaged here; the interface requires it.</summary>
    public void Dispose() => GC.SuppressFinalize(this);

    public bool RenderMark(ListView listView, int item, int row, bool isMarked, bool markMultiple) => false;

    public void Render(
        ListView listView,
        bool selected,
        int item,
        int col,
        int row,
        int width,
        int viewportX)
    {
        if (width <= 0)
        {
            return;
        }

        _lastRenderWidth = width;

        var entry = this[item];
        if (entry is null)
        {
            return;
        }

        var scheme = ThemeCatalog.Resolve("Sidebar");

        if (entry.IsHeader)
        {
            // Headers borrow the accent colour and never show as selected.
            listView.Move(col, row);
            listView.SetAttribute(ThemeCatalog.Resolve("Accent").Normal with
            {
                Background = scheme.Normal.Background
            });
            listView.AddStr(Fit(" " + entry.Label, width));
            return;
        }

        if (entry.Kind == SidebarKind.ToggleFeeds)
        {
            // Reads as an action, not a feed you can select.
            listView.Move(col, row);
            listView.SetAttribute(selected
                ? scheme.Focus
                : ThemeCatalog.Resolve("Accent").Normal with { Background = scheme.Normal.Background });

            listView.AddStr(Fit($"  {entry.Label}", width));
            return;
        }

        listView.Move(col, row);
        listView.SetAttribute(selected ? scheme.Focus : scheme.Normal);

        var count = entry.Count is { } n ? n.ToString(CultureInfo.InvariantCulture) : "";
        var marker = selected ? _glyphs.Selected : " ";
        var labelWidth = Math.Max(1, width - count.Length - 3);
        var label = entry.Label.Length > labelWidth
            ? entry.Label[..Math.Max(1, labelWidth - 1)] + "…"
            : entry.Label.PadRight(labelWidth);

        listView.AddStr(Fit($"{marker} {label} {count}", width));
    }

    private static string Fit(string value, int width) =>
        value.Length >= width ? value[..width] : value.PadRight(width);
}
