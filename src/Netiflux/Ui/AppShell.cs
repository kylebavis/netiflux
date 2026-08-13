using System.Diagnostics;
using System.Globalization;
using Netiflux.Core;
using Netiflux.Core.Configuration;
using Netiflux.Core.Models;
using Netiflux.Core.Text;
using Netiflux.Services;
using Netiflux.Theming;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Netiflux.Ui;

/// <summary>How much of the screen the reading and triage panes get.</summary>
public enum PaneMode
{
    /// <summary>List and reader side by side — the default on a wide terminal.</summary>
    Split,

    /// <summary>Only the entry list. Used on narrow terminals before an entry is opened.</summary>
    ListOnly,

    /// <summary>Only the reader. Reached with <c>z</c>, or on narrow terminals after opening.</summary>
    ReaderOnly
}

/// <summary>The main window: navigation rail, entry list, reader, and the key map that ties them together.</summary>
public sealed partial class AppShell : Window
{
    /// <summary>Below this width three panes stop being readable and the UI goes single-pane.</summary>
    private const int NarrowThreshold = 100;

    /// <summary>How long the cursor must rest on an entry before the reader renders it.</summary>
    private static readonly TimeSpan ReaderDebounce = TimeSpan.FromMilliseconds(120);

    private readonly ReaderSession _session;
    private readonly NetifluxConfig _config;
    private readonly GlyphSet _glyphs;

    private readonly Label _banner;
    private readonly FrameView _sidebarFrame;
    private readonly ListView _sidebar;
    private readonly SidebarSource _sidebarSource;
    private readonly FrameView _listFrame;
    private readonly ListView _list;
    private readonly EntryListSource _listSource;
    private readonly FrameView _readerFrame;
    private readonly Markdown _reader;
    private readonly Label _searchPrompt;
    private readonly TextField _searchField;
    private readonly StatusLine _status;

    private PaneMode _paneMode = PaneMode.Split;
    private bool _sidebarVisible;
    private bool _busy;
    private Key? _pendingChord;
    private object? _chordTimeout;
    private Entry? _openEntry;
    private object? _readerDebounce;

    /// <summary>
    /// Whether mark-on-select is live. Disarmed after a list load so the initial cursor
    /// position is not treated as a read, and re-armed by the first key the user presses.
    /// </summary>
    private bool _autoMarkArmed;
    private IApplication? _attachedApp;
    private bool _searchVisible;

    /// <summary>Set while the sidebar cursor is moved programmatically, to avoid a reload.</summary>
    private bool _suppressSidebarLoad;

    /// <summary>
    /// The view to come back to when search results are dismissed. Captured when a search
    /// runs, so Esc returns you to whatever you were reading rather than stranding you.
    /// </summary>
    private ViewRestorePoint? _restorePoint;

    private CancellationTokenSource _operationCts = new();

    /// <summary>Enough state to put the entry list back the way it was.</summary>
    private sealed record ViewRestorePoint(int SidebarIndex, EntryQuery Query, string Title);

    public AppShell(ReaderSession session)
    {
        _session = session;
        _config = session.Config;
        _glyphs = GlyphSet.Detect();
        _sidebarVisible = _config.ShowSidebar;

        Title = "Netiflux";
        BorderStyle = Terminal.Gui.Drawing.LineStyle.None;
        SchemeName = "Base";

        _banner = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = "Banner",
            CanFocus = false
        };

        _sidebarSource = new SidebarSource(_glyphs);
        _sidebar = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = _sidebarSource,
            SchemeName = "Sidebar",
            ShowMarks = false
        };

        _sidebarFrame = new FrameView
        {
            Title = "Feeds",
            SchemeName = "Sidebar",
            SuperViewRendersLineCanvas = true,
            CanFocus = true
        };
        _sidebarFrame.Add(_sidebar);

        _listSource = new EntryListSource(session.SavedStore, _glyphs);
        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Source = _listSource,
            SchemeName = "Base",
            ShowMarks = false
        };

        _listFrame = new FrameView
        {
            Title = "Entries",
            SchemeName = "Base",
            SuperViewRendersLineCanvas = true,
            CanFocus = true
        };
        _listFrame.Add(_list);

        _reader = new Markdown
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            SchemeName = "Reader",
            Text = WelcomeText(),
            ShowCopyButtons = false,
            UseThemeBackground = true
        };

        _readerFrame = new FrameView
        {
            Title = "Reader",
            SchemeName = "Reader",
            SuperViewRendersLineCanvas = true,
            CanFocus = true
        };
        _readerFrame.Add(_reader);

        _status = new StatusLine { Y = Pos.AnchorEnd(1) };

        // The search prompt is inline rather than a modal dialog. A dialog hid the very
        // thing being typed behind its own focus handling; a bar in the window shows the
        // query as it is entered, the way `/` does in a pager.
        _searchPrompt = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = 2,
            Height = 1,
            Text = " /",
            SchemeName = "Accent",
            CanFocus = false,
            Visible = false
        };

        _searchField = new TextField
        {
            X = 2,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(),
            Height = 1,
            SchemeName = "Base",
            Visible = false
        };

        _searchField.KeyDown += OnSearchFieldKeyDown;

        Add(_banner, _sidebarFrame, _listFrame, _readerFrame, _searchPrompt, _searchField, _status);

        // Sidebar headers are not selectable; nudge the cursor past them.
        _sidebar.ValueChanged += OnSidebarSelectionChanged;
        _list.ValueChanged += OnListSelectionChanged;

        SubViewsLaidOut += (_, _) =>
        {
            ApplyResponsiveMode();

            // Width is unknown until the first layout, and the legend depends on it.
            UpdateLegend();
        };

        ApplyLayout();
        UpdateLegend();

        // Start with the entry list live, so triage keys work the moment the app opens.
        Initialized += (_, _) => _list.SetFocus();
    }

    /// <summary>Raised when the user asks to quit so the host can tear the session down.</summary>
    public event EventHandler? QuitRequested;

    public StatusLine Status => _status;

    /// <summary>Whether the inline search bar is currently open.</summary>
    public bool SearchBarVisible => _searchVisible;

    /// <summary>Text currently typed into the search bar.</summary>
    public string SearchText => _searchField.Text ?? "";

    /// <summary>Caption above the entry list, e.g. "Unread (16)" or "Search: rust (4)".</summary>
    public string ListTitle => _listFrame.Title ?? "";

    /// <summary>True while search results are showing and Esc would return to the prior view.</summary>
    public bool ShowingSearchResults => _restorePoint is not null;

    /// <summary>True when rows are marked for a batch action.</summary>
    public bool HasMarkedEntries => _listSource.HasMarks;

    /// <summary>How the panes are currently arranged.</summary>
    public PaneMode PaneLayout => _paneMode;

    /// <summary>
    /// Subscribes the triage key map at application scope. Must be called before the
    /// shell is run.
    /// <para>
    /// Keys cannot be handled on the panes themselves: each pane lives inside a
    /// <see cref="FrameView"/>, and it is the frame that takes focus, so a view-level
    /// <c>KeyDown</c> handler never sees the keystroke. Application scope also runs ahead
    /// of <see cref="ListView"/>'s type-ahead navigator, which would otherwise consume
    /// j/k/m/f/s as search characters.
    /// </para>
    /// </summary>
    public void AttachTo(IApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (_attachedApp is not null)
        {
            _attachedApp.Keyboard.KeyDown -= OnAppKeyDown;
        }

        _attachedApp = app;
        app.Keyboard.KeyDown += OnAppKeyDown;
    }

    private static string WelcomeText() =>
        """
        # Netiflux

        Loading your feeds…

        Press `?` at any time for the full key map, or `q` to quit.
        """;

    // ---------------------------------------------------------------- layout

    private void ApplyLayout()
    {
        var showSidebar = _sidebarVisible && _paneMode != PaneMode.ReaderOnly;
        var showList = _paneMode is PaneMode.Split or PaneMode.ListOnly;
        var showReader = _paneMode is PaneMode.Split or PaneMode.ReaderOnly;

        _sidebarFrame.Visible = showSidebar;
        _listFrame.Visible = showList;
        _readerFrame.Visible = showReader;

        _searchPrompt.Visible = _searchVisible;
        _searchField.Visible = _searchVisible;

        // Panes give up a row to the search bar rather than being overdrawn by it.
        var bottomReserve = _searchVisible ? 2 : 1;

        _sidebarFrame.X = 0;
        _sidebarFrame.Y = 1;
        _sidebarFrame.Width = Math.Max(16, _config.SidebarWidth);
        _sidebarFrame.Height = Dim.Fill(bottomReserve);

        // Panes overlap by one column so their borders share a line.
        var listX = showSidebar ? Pos.Right(_sidebarFrame) - 1 : 0;

        _listFrame.X = listX;
        _listFrame.Y = 1;
        _listFrame.Height = Dim.Fill(bottomReserve);

        if (showList && showReader)
        {
            var percent = Math.Clamp(_config.ListWidthPercent, 20, 70);
            _listFrame.Width = Dim.Percent(percent);
            _readerFrame.X = Pos.Right(_listFrame) - 1;
            _readerFrame.Width = Dim.Fill();
        }
        else if (showList)
        {
            _listFrame.Width = Dim.Fill();
        }

        if (showReader && !showList)
        {
            _readerFrame.X = showSidebar ? Pos.Right(_sidebarFrame) - 1 : 0;
            _readerFrame.Width = Dim.Fill();
        }

        _readerFrame.Y = 1;
        _readerFrame.Height = Dim.Fill(bottomReserve);

        UpdateLegend();
        SetNeedsDraw();
    }

    /// <summary>
    /// Collapses to a single pane on narrow terminals. Rather than squeezing three
    /// columns into 80 cells, the list and reader take turns: opening an entry swaps to
    /// the reader, and Esc comes back.
    /// </summary>
    private void ApplyResponsiveMode()
    {
        var narrow = Viewport.Width > 0 && Viewport.Width < NarrowThreshold;

        if (narrow && _paneMode == PaneMode.Split)
        {
            _paneMode = _openEntry is null ? PaneMode.ListOnly : PaneMode.ReaderOnly;
            if (_sidebarVisible)
            {
                _sidebarVisible = false;
            }

            ApplyLayout();
        }
        else if (!narrow && _paneMode == PaneMode.ListOnly)
        {
            _paneMode = PaneMode.Split;
            ApplyLayout();
        }
    }

    private bool IsNarrow => Viewport.Width > 0 && Viewport.Width < NarrowThreshold;

    // ------------------------------------------------------------ data loading

    public async Task InitialLoadAsync()
    {
        await RunOperationAsync("Loading feeds…", async ct =>
        {
            await _session.LoadNavigationAsync(ct).ConfigureAwait(false);
            await _session.LoadEntriesAsync(SelectedSidebarQuery(), ct).ConfigureAwait(false);
        }, onSuccess: () =>
        {
            RebuildSidebar();
            RefreshListFromSession(selectFirst: true);
            _status.Show($"{_session.UnreadTotal} unread", ToastKind.Info);
        }).ConfigureAwait(false);
    }

    private EntryQuery SelectedSidebarQuery()
    {
        var item = _sidebarSource[_sidebar.SelectedItem ?? 1];
        return (item is null || item.IsHeader)
            ? new EntryQuery { Statuses = [EntryStatus.Unread], Limit = _config.PageSize }
            : item.ToQuery(_config.PageSize);
    }

    private void RebuildSidebar()
    {
        var selected = _sidebar.SelectedItem;

        _sidebarSource.Rebuild(
            _session.Categories,
            _session.Feeds,
            _session.UnreadTotal,
            _session.StarredTotal,
            _session.FeedUnread);

        _sidebar.SetNeedsDraw();

        if (selected is { } index && index < _sidebarSource.Count)
        {
            _sidebar.SelectedItem = index;
        }
        else
        {
            _sidebar.SelectedItem = 1;
        }
    }

    private void RefreshListFromSession(bool selectFirst)
    {
        // Landing on row 0 of a freshly loaded list is not a read.
        _autoMarkArmed = false;

        _listSource.SetEntries(_session.Entries);
        _list.SetNeedsDraw();

        if (selectFirst && _session.Entries.Count > 0)
        {
            _list.SelectedItem = 0;
            ShowEntryInReader(_session.Entries[0], markRead: false);
        }
        else if (_session.Entries.Count == 0)
        {
            _openEntry = null;
            _reader.Text = "# Nothing here\n\n*No entries match this view.*";
            _readerFrame.Title = "Reader";
        }

        UpdateBanner();
        UpdateListTitle();
    }

    private void UpdateBanner()
    {
        var view = CurrentViewLabel();
        var parts = new List<string>
        {
            " Netiflux",
            view,
            $"{_session.UnreadTotal.ToString(CultureInfo.CurrentCulture)} unread"
        };

        if (_session.StarredTotal > 0)
        {
            parts.Add($"{_session.StarredTotal.ToString(CultureInfo.CurrentCulture)} starred");
        }

        if (_listSource.HasMarks)
        {
            parts.Add($"{_listSource.MarkedEntries.Count.ToString(CultureInfo.CurrentCulture)} selected");
        }

        _banner.Text = string.Join("  ·  ", parts);
    }

    private string CurrentViewLabel()
    {
        var item = _sidebarSource[_sidebar.SelectedItem ?? 1];
        return item is null || item.IsHeader ? "Unread" : item.Label;
    }

    private void UpdateListTitle()
    {
        var shown = _listSource.Count;
        var total = _session.TotalMatching;
        var suffix = total > shown ? $"{shown}/{total}" : shown.ToString(CultureInfo.CurrentCulture);
        _listFrame.Title = $"{CurrentViewLabel()} ({suffix})";
    }

    /// <summary>
    /// Rewrites the key legend for the current mode. This is the only guidance that stays
    /// on screen — the welcome text in the reader is replaced by the first article — so
    /// the less guessable bindings earn a place here, and the list is trimmed rather than
    /// truncated when the terminal is narrow.
    /// </summary>
    private void UpdateLegend()
    {
        if (_searchVisible)
        {
            _status.Legend = "  type to search · ⏎ run · Esc cancel";
            return;
        }

        // "? help" and "q quit" appear in every variant: they are the two things a user
        // who is stuck needs, and the only ones with no other way to discover them.
        const string Escapes = "? help · q quit";

        var width = Viewport.Width;
        var roomy = width is 0 or >= 120;
        var medium = width is 0 or >= 96;

        if (_restorePoint is not null && _paneMode != PaneMode.ReaderOnly)
        {
            _status.Legend = $"  j/k move · ⏎ open · s save · f star · Esc leave search · {Escapes}";
            return;
        }

        if (_paneMode == PaneMode.ReaderOnly)
        {
            _status.Legend = roomy
                ? $"  j/k scroll · Esc back · m read · f star · s save · v browser · {Escapes}"
                : $"  j/k scroll · Esc back · s save · {Escapes}";

            return;
        }

        if (roomy)
        {
            _status.Legend =
                $"  j/k move · ⏎ open · m read · f star · s save · v browser · / search · Ctrl+T theme · {Escapes}";
            return;
        }

        _status.Legend = medium
            ? $"  j/k move · ⏎ open · m read · f star · s save · / search · {Escapes}"
            : $"  j/k move · ⏎ open · s save · {Escapes}";
    }

    // ------------------------------------------------------------- selection

    private void OnSidebarSelectionChanged(object? sender, ValueChangedEventArgs<int?> args)
    {
        if (args.NewValue is not { } index)
        {
            return;
        }

        var item = _sidebarSource[index];
        if (item is null)
        {
            return;
        }

        if (item.IsHeader)
        {
            // Skip in whichever direction the cursor was travelling.
            var direction = index > (args.OldValue ?? 0) ? 1 : -1;
            var next = _sidebarSource.NextSelectable(index, direction);
            if (next != index)
            {
                _sidebar.SelectedItem = next;
            }

            return;
        }

        // The feed-list toggle is a landable row but not a view; pressing Enter runs it.
        if (item.IsInert || _suppressSidebarLoad)
        {
            return;
        }

        // Choosing a view from the rail ends any search that was showing.
        _restorePoint = null;
        LoadSidebarSelection(item);
    }

    private void LoadSidebarSelection(SidebarItem item)
    {
        _ = RunOperationAsync($"Loading {item.Label}…", ct =>
            _session.LoadEntriesAsync(item.ToQuery(_config.PageSize), ct),
            onSuccess: () => RefreshListFromSession(selectFirst: true));
    }

    private void OnListSelectionChanged(object? sender, ValueChangedEventArgs<int?> args)
    {
        if (args.NewValue is not { } index)
        {
            return;
        }

        var entry = _listSource[index];
        if (entry is null)
        {
            return;
        }

        ScheduleSettle(entry);
        UpdateBanner();

        // Page in more entries as the cursor approaches the end of what is loaded.
        if (index >= _listSource.Count - 5 && _session.HasMore && !_busy)
        {
            _ = RunOperationAsync("Loading more…", async ct =>
            {
                await _session.LoadMoreAsync(ct).ConfigureAwait(false);
            }, onSuccess: () =>
            {
                _listSource.SetEntries(_session.Entries);
                _list.SetNeedsDraw();
                UpdateListTitle();
            }, quiet: true);
        }
    }

    private Entry? SelectedEntry => _listSource[_list.SelectedItem ?? -1];

    /// <summary>Entries an action applies to: the marked set if any, otherwise the cursor.</summary>
    private IReadOnlyList<Entry> ActionTargets()
    {
        if (_listSource.HasMarks)
        {
            return _listSource.MarkedEntries;
        }

        var current = _paneMode == PaneMode.ReaderOnly ? _openEntry ?? SelectedEntry : SelectedEntry;
        return current is null ? [] : [current];
    }

    // ---------------------------------------------------------------- reader

    /// <summary>
    /// Runs the work that should happen once the cursor stops on an entry: refreshing the
    /// reader and, under the default rule, marking it read.
    /// <para>
    /// Both are deferred briefly. Holding <c>j</c> would otherwise run an
    /// HTML-to-Markdown conversion for every headline the cursor flies past, and — worse —
    /// mark a whole screenful read just because you scrolled through it to reach the
    /// bottom. Settling on an entry is the signal; passing over it is not.
    /// </para>
    /// </summary>
    private void ScheduleSettle(Entry entry)
    {
        if (_readerDebounce is not null)
        {
            App?.RemoveTimeout(_readerDebounce);
            _readerDebounce = null;
        }

        if (App is not { } app)
        {
            ApplySettle(entry);
            return;
        }

        _readerDebounce = app.AddTimeout(ReaderDebounce, () =>
        {
            _readerDebounce = null;

            // Only act if the cursor is still on this entry.
            if (ReferenceEquals(SelectedEntry, entry))
            {
                ApplySettle(entry);
            }

            return false;
        });
    }

    private void ApplySettle(Entry entry)
    {
        if (_paneMode == PaneMode.Split)
        {
            ShowEntryInReader(entry, markRead: false);
        }

        // Only after the user has actually moved. Otherwise opening the app would mark
        // the top article read before it had been looked at.
        if (_autoMarkArmed && _config.AutoMarkRead == AutoMarkRead.OnSelect && entry.IsUnread)
        {
            MarkEntries([entry], EntryStatus.Read, quiet: true);
        }
    }

    private void ShowEntryInReader(Entry entry, bool markRead)
    {
        _openEntry = entry;
        _reader.Text = ArticleRenderer.Render(entry);
        _readerFrame.Title = Ellipsize(entry.Title, Math.Max(10, _readerFrame.Viewport.Width - 4));
        _reader.Viewport = _reader.Viewport with { Y = 0 };
        _reader.SetNeedsDraw();

        if (markRead || _config.AutoMarkRead == AutoMarkRead.OnOpen)
        {
            MarkEntries([entry], EntryStatus.Read, quiet: true);
        }

        if (_config.AutoFetchTruncated && ArticleRenderer.LooksTruncated(entry))
        {
            FetchFullText(entry, quiet: true);
        }
    }

    private static string Ellipsize(string value, int max)
    {
        if (max <= 1 || string.IsNullOrEmpty(value))
        {
            return value ?? "";
        }

        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }

    private void OpenSelected()
    {
        // Enter in the sidebar activates the row rather than jumping to the reader.
        if (_sidebarFrame.HasFocus)
        {
            ActivateSidebarRow();
            return;
        }

        var entry = SelectedEntry;
        if (entry is null)
        {
            return;
        }

        if (IsNarrow || _paneMode == PaneMode.ListOnly)
        {
            _paneMode = PaneMode.ReaderOnly;
            ApplyLayout();
        }

        ShowEntryInReader(entry, markRead: _config.AutoMarkRead == AutoMarkRead.OnOpen);
        _reader.SetFocus();
    }

    /// <summary>Runs the row under the sidebar cursor: expand/collapse, or load a view.</summary>
    private void ActivateSidebarRow()
    {
        var item = _sidebarSource[_sidebar.SelectedItem ?? -1];
        if (item is null || item.IsHeader)
        {
            return;
        }

        if (item.Kind == SidebarKind.ToggleFeeds)
        {
            var cursor = _sidebar.SelectedItem ?? 0;
            _sidebarSource.ToggleShowAllFeeds();
            RebuildSidebar();

            // Keep the cursor on the toggle so it can be pressed straight back.
            _sidebar.SelectedItem = Math.Min(cursor, Math.Max(0, _sidebarSource.Count - 1));
            _status.Show(_sidebarSource.ShowAllFeeds ? "Showing all feeds" : "Showing feeds with unread");
            return;
        }

        LoadSidebarSelection(item);
        _list.SetFocus();
    }

    private void BackToList()
    {
        if (_paneMode != PaneMode.ReaderOnly)
        {
            _list.SetFocus();
            return;
        }

        _paneMode = IsNarrow ? PaneMode.ListOnly : PaneMode.Split;
        ApplyLayout();
        _list.SetFocus();
    }

    private void ToggleZen()
    {
        _paneMode = _paneMode == PaneMode.ReaderOnly
            ? (IsNarrow ? PaneMode.ListOnly : PaneMode.Split)
            : PaneMode.ReaderOnly;

        ApplyLayout();

        if (_paneMode == PaneMode.ReaderOnly)
        {
            _reader.SetFocus();
        }
        else
        {
            _list.SetFocus();
        }
    }

    /// <summary>True when the reader is scrolled to the last line of the article.</summary>
    private bool ReaderAtEnd()
    {
        var content = _reader.GetContentSize();
        var viewport = _reader.Viewport;
        return content.Height <= viewport.Height || viewport.Y + viewport.Height >= content.Height;
    }

    private void CheckScrollEndMarking()
    {
        if (_config.AutoMarkRead != AutoMarkRead.OnScrollEnd || _openEntry is not { } entry)
        {
            return;
        }

        if (entry.IsUnread && ReaderAtEnd())
        {
            MarkEntries([entry], EntryStatus.Read, quiet: true);
        }
    }
}
