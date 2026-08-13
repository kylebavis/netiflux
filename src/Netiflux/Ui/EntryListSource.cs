using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using Netiflux.Core.Models;
using Netiflux.Core.State;
using Netiflux.Theming;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;

namespace Netiflux.Ui;

/// <summary>
/// Draws the entry list. Rows are rendered by hand rather than through the default
/// string source so read state, star and "already pushed to my bookmark service" can
/// each carry their own colour — during triage those three facts are the whole point of
/// the list, and they need to be readable at a glance without moving the cursor.
/// </summary>
public sealed class EntryListSource : IListDataSource
{
    private const int FeedColumnWidth = 20;
    private const int AgeColumnWidth = 4;

    private readonly List<Entry> _entries = [];
    private readonly HashSet<int> _marked = [];
    private readonly SavedEntryStore _savedStore;
    private GlyphSet _glyphs;
    private int _lastRenderWidth = 80;

    public EntryListSource(SavedEntryStore savedStore, GlyphSet glyphs)
    {
        _savedStore = savedStore;
        _glyphs = glyphs;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int Count => _entries.Count;

    /// <summary>ListView uses this for its content width; rows always fit, so report the viewport.</summary>
    public int MaxItemLength => _lastRenderWidth;

    public bool SuspendCollectionChangedEvent { get; set; }

    public IReadOnlyList<Entry> Entries => _entries;

    public GlyphSet Glyphs
    {
        get => _glyphs;
        set => _glyphs = value;
    }

    public Entry? this[int index] =>
        index >= 0 && index < _entries.Count ? _entries[index] : null;

    public void SetEntries(IEnumerable<Entry> entries)
    {
        _entries.Clear();
        _marked.Clear();
        _entries.AddRange(entries);
        RaiseCollectionChanged();
    }

    public void Append(IEnumerable<Entry> entries)
    {
        _entries.AddRange(entries);
        RaiseCollectionChanged();
    }

    /// <summary>Drops an entry from view — used when a filter no longer matches it.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        _entries.RemoveAt(index);
        _marked.Remove(index);

        // Marks are positional, so everything after the removal shifts down one.
        var shifted = _marked.Where(i => i > index).ToList();
        foreach (var i in shifted)
        {
            _marked.Remove(i);
            _marked.Add(i - 1);
        }

        RaiseCollectionChanged();
    }

    public void RaiseCollectionChanged()
    {
        if (SuspendCollectionChangedEvent)
        {
            return;
        }

        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IReadOnlyList<Entry> MarkedEntries =>
        _marked.Where(i => i < _entries.Count).OrderBy(i => i).Select(i => _entries[i]).ToList();

    public bool HasMarks => _marked.Count > 0;

    public void ClearMarks()
    {
        _marked.Clear();
        RaiseCollectionChanged();
    }

    public bool IsMarked(int item) => _marked.Contains(item);

    public void SetMark(int item, bool value)
    {
        if (value)
        {
            _marked.Add(item);
        }
        else
        {
            _marked.Remove(item);
        }
    }

    public IList ToList() => _entries;

    /// <summary>Nothing unmanaged here; the interface requires it.</summary>
    public void Dispose() => GC.SuppressFinalize(this);

    /// <summary>The mark column is drawn as part of <see cref="Render"/>, not separately.</summary>
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

        var isSaved = _savedStore.IsSaved(entry.Id);
        var scheme = SchemeFor(entry, isSaved);
        var attribute = selected ? scheme.Focus : scheme.Normal;

        // A dimmed attribute for the trailing metadata, but only when this row is not the
        // selection: on the highlight bar a second colour reads as a rendering glitch.
        var metaAttribute = selected
            ? attribute
            : ThemeCatalog.Resolve("EntryRead").Normal with { Background = attribute.Background };

        listView.Move(col, row);
        listView.SetAttribute(attribute);

        var text = BuildRowText(entry, item, isSaved, width, out var metaStartIndex);

        if (metaStartIndex <= 0 || metaStartIndex >= text.Length)
        {
            listView.AddStr(text);
            return;
        }

        listView.AddStr(text[..metaStartIndex]);
        listView.SetAttribute(metaAttribute);
        listView.AddStr(text[metaStartIndex..]);
    }

    private Scheme SchemeFor(Entry entry, bool isSaved)
    {
        if (isSaved)
        {
            return ThemeCatalog.Resolve("EntrySaved");
        }

        return entry.IsUnread
            ? ThemeCatalog.Resolve("EntryUnread")
            : ThemeCatalog.Resolve("EntryRead");
    }

    /// <summary>
    /// Lays out one row as: marks, state glyph, title, feed, age. Returns the index at
    /// which the dimmable trailing metadata begins so <see cref="Render"/> can recolour it.
    /// </summary>
    private string BuildRowText(Entry entry, int index, bool isSaved, int width, out int metaStartIndex)
    {
        var marker = _marked.Contains(index) ? _glyphs.Selected : " ";
        var state = entry.IsUnread ? _glyphs.Unread : _glyphs.Read;

        var flag = (isSaved, entry.Starred) switch
        {
            (true, true) => _glyphs.SavedAndStarred,
            (true, false) => _glyphs.Saved,
            (false, true) => _glyphs.Starred,
            _ => " "
        };

        var prefix = $"{marker}{state}{flag} ";

        var age = FormatAge(entry.PublishedAt).PadLeft(AgeColumnWidth);
        var feedWidth = width >= 70 ? FeedColumnWidth : 0;
        var feed = feedWidth > 0 ? Truncate(entry.FeedTitle, feedWidth - 1).PadRight(feedWidth) : "";

        var titleWidth = width - prefix.Length - feed.Length - age.Length - 1;
        if (titleWidth < 8)
        {
            // Very narrow pane: title only.
            metaStartIndex = -1;
            return Truncate(prefix + Sanitize(entry.Title), width);
        }

        var title = Truncate(Sanitize(entry.Title), titleWidth).PadRight(titleWidth);
        metaStartIndex = prefix.Length + title.Length;

        return prefix + title + " " + feed + age;
    }

    /// <summary>Titles arrive with entities already decoded but can still carry newlines and tabs.</summary>
    private static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "(untitled)";
        }

        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            buffer[i] = char.IsControl(c) ? ' ' : c;
        }

        return new string(buffer).Trim();
    }

    private static string Truncate(string value, int max)
    {
        if (max <= 0)
        {
            return "";
        }

        if (value.Length <= max)
        {
            return value;
        }

        return max <= 1 ? value[..max] : value[..(max - 1)] + "…";
    }

    /// <summary>Compact relative age: 45m, 6h, 3d, 2w, 5mo.</summary>
    internal static string FormatAge(DateTimeOffset published)
    {
        var delta = DateTimeOffset.UtcNow - published.ToUniversalTime();

        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 60)
        {
            return Math.Max(1, (int)delta.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m";
        }

        if (delta.TotalHours < 24)
        {
            return ((int)delta.TotalHours).ToString(CultureInfo.InvariantCulture) + "h";
        }

        if (delta.TotalDays < 7)
        {
            return ((int)delta.TotalDays).ToString(CultureInfo.InvariantCulture) + "d";
        }

        if (delta.TotalDays < 365)
        {
            var weeks = (int)(delta.TotalDays / 7);
            return weeks < 9
                ? weeks.ToString(CultureInfo.InvariantCulture) + "w"
                : ((int)(delta.TotalDays / 30)).ToString(CultureInfo.InvariantCulture) + "mo";
        }

        return ((int)(delta.TotalDays / 365)).ToString(CultureInfo.InvariantCulture) + "y";
    }
}
