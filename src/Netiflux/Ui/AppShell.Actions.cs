using System.Diagnostics;
using Netiflux.Core;
using Netiflux.Core.Configuration;
using Netiflux.Core.Models;
using Netiflux.Core.Text;
using Netiflux.Theming;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Netiflux.Ui;

/// <summary>
/// Key handling and the actions behind it.
/// <para>
/// The bindings deliberately mirror Miniflux's own web shortcuts (j/k, o, m, f, s, v,
/// g-chords). Anyone arriving from the web UI already has these in their fingers, and
/// inventing a second vocabulary for the same product would be a needless tax.
/// </para>
/// </summary>
public sealed partial class AppShell
{
    private static readonly TimeSpan ChordTimeout = TimeSpan.FromMilliseconds(900);

    /// <summary>
    /// Matches a printable key. Punctuation has no named constant on <see cref="Key"/>,
    /// and comparing the rune also naturally excludes Ctrl/Alt combinations.
    /// </summary>
    private static bool IsChar(Key key, char expected) =>
        !key.IsCtrl && !key.IsAlt && key.AsRune.Value == expected;

    /// <summary>
    /// Application-scope key handler. Defers to whatever is on top when a dialog is open,
    /// so typing in the search box or help window is never intercepted.
    /// </summary>
    private void OnAppKeyDown(object? sender, Key key)
    {
        if (_attachedApp is { } app && !ReferenceEquals(app.TopRunnable, this))
        {
            return;
        }

        // While the search bar is open every keystroke belongs to it, or the query would
        // be swallowed by the triage bindings instead of appearing in the field.
        if (_searchVisible)
        {
            return;
        }

        OnShellKeyDown(sender, key);
    }

    private void OnShellKeyDown(object? sender, Key key)
    {
        // Any deliberate keystroke means the user is engaging with the list, so
        // mark-on-select becomes live from here on.
        _autoMarkArmed = true;

        if (_pendingChord is not null)
        {
            var handled = HandleChord(key);
            ClearChord();
            key.Handled = handled;
            return;
        }

        key.Handled = HandleKey(key);
    }

    private bool HandleKey(Key key)
    {
        // Movement first: these fire constantly during triage.
        if (key == Key.J || key == Key.CursorDown.WithCtrl)
        {
            return MoveSelection(1);
        }

        if (key == Key.K || key == Key.CursorUp.WithCtrl)
        {
            return MoveSelection(-1);
        }

        if (key == Key.G.WithShift)
        {
            return MoveToEnd();
        }

        if (key == Key.G)
        {
            BeginChord(key);
            return true;
        }

        if (key == Key.Enter || key == Key.O)
        {
            OpenSelected();
            return true;
        }

        if (key == Key.Esc)
        {
            // Unwind one layer at a time: selection, then the reader, then search.
            if (_listSource.HasMarks)
            {
                _listSource.ClearMarks();
                _list.SetNeedsDraw();
                UpdateBanner();
                _status.Show("Selection cleared");
                return true;
            }

            if (_paneMode == PaneMode.ReaderOnly)
            {
                BackToList();
                return true;
            }

            if (_restorePoint is not null)
            {
                LeaveSearchResults();
                return true;
            }

            BackToList();
            return true;
        }

        if (key == Key.Space)
        {
            return HandleSpace();
        }

        if (key == Key.M)
        {
            ToggleRead();
            return true;
        }

        if (key == Key.F)
        {
            ToggleStar();
            return true;
        }

        if (key == Key.S)
        {
            SaveToBookmarkService();
            return true;
        }

        if (key == Key.V)
        {
            OpenInBrowser();
            return true;
        }

        if (key == Key.R)
        {
            ReloadCurrentView();
            return true;
        }

        if (key == Key.R.WithShift)
        {
            RefreshFeedsOnServer();
            return true;
        }

        if (key == Key.A.WithShift)
        {
            MarkAllRead();
            return true;
        }

        if (key == Key.F.WithShift)
        {
            FetchFullText(_openEntry ?? SelectedEntry, quiet: false);
            return true;
        }

        if (key == Key.Z)
        {
            ToggleZen();
            return true;
        }

        if (IsChar(key, '\\') || key == Key.B.WithCtrl)
        {
            _sidebarVisible = !_sidebarVisible;
            ApplyLayout();
            return true;
        }

        if (key == Key.Tab)
        {
            CyclePane(1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            CyclePane(-1);
            return true;
        }

        if (IsChar(key, '/'))
        {
            ShowSearchBar();
            return true;
        }

        if (key == Key.T.WithCtrl)
        {
            ShowThemePicker();
            return true;
        }

        if (IsChar(key, '?') || key == Key.F1)
        {
            HelpDialog.Show(this, _glyphs);
            return true;
        }

        if (key == Key.Q || key == Key.Q.WithCtrl)
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    // ----------------------------------------------------------------- chords

    private void BeginChord(Key key)
    {
        _pendingChord = key;
        _status.Show("g … (u unread · t today · s starred · a all · g top)");

        _chordTimeout = App?.AddTimeout(ChordTimeout, () =>
        {
            ClearChord();
            return false;
        });
    }

    private void ClearChord()
    {
        _pendingChord = null;

        if (_chordTimeout is not null)
        {
            App?.RemoveTimeout(_chordTimeout);
            _chordTimeout = null;
        }
    }

    private bool HandleChord(Key key)
    {
        if (key == Key.G)
        {
            _list.SelectedItem = 0;
            return true;
        }

        var kind = key == Key.U ? SidebarKind.Unread
            : key == Key.T ? SidebarKind.Today
            : key == Key.S ? SidebarKind.Starred
            : key == Key.A ? SidebarKind.All
            : (SidebarKind?)null;

        if (kind is null)
        {
            _status.ClearToast();
            return false;
        }

        JumpToView(kind.Value);
        return true;
    }

    private void JumpToView(SidebarKind kind)
    {
        for (var i = 0; i < _sidebarSource.Count; i++)
        {
            if (_sidebarSource[i]?.Kind == kind)
            {
                _sidebar.SelectedItem = i;
                return;
            }
        }
    }

    // -------------------------------------------------------------- movement

    private bool MoveSelection(int delta)
    {
        // In the reader, j/k scroll the article rather than changing entry.
        if (_paneMode == PaneMode.ReaderOnly || _readerFrame.HasFocus)
        {
            ScrollReader(delta);
            return true;
        }

        if (_sidebarFrame.HasFocus)
        {
            var current = _sidebar.SelectedItem ?? 0;
            var next = _sidebarSource.NextSelectable(
                Math.Clamp(current + delta, 0, Math.Max(0, _sidebarSource.Count - 1)),
                delta >= 0 ? 1 : -1);

            _sidebar.SelectedItem = next;
            return true;
        }

        if (_listSource.Count == 0)
        {
            return true;
        }

        var index = Math.Clamp((_list.SelectedItem ?? 0) + delta, 0, _listSource.Count - 1);
        _list.SelectedItem = index;
        _list.EnsureSelectedItemVisible();
        return true;
    }

    private bool MoveToEnd()
    {
        if (_listSource.Count > 0 && !_readerFrame.HasFocus)
        {
            _list.SelectedItem = _listSource.Count - 1;
            _list.EnsureSelectedItemVisible();
        }

        return true;
    }

    private void ScrollReader(int delta)
    {
        var viewport = _reader.Viewport;
        var content = _reader.GetContentSize();
        var maxY = Math.Max(0, content.Height - viewport.Height);

        _reader.Viewport = viewport with { Y = Math.Clamp(viewport.Y + delta, 0, maxY) };
        _reader.SetNeedsDraw();
        CheckScrollEndMarking();
    }

    private bool HandleSpace()
    {
        // Space pages through an article being read, and marks rows while triaging.
        if (_paneMode == PaneMode.ReaderOnly || _readerFrame.HasFocus)
        {
            ScrollReader(Math.Max(1, _reader.Viewport.Height - 2));
            return true;
        }

        if (_listSource.Count == 0 || _list.SelectedItem is not { } index)
        {
            return true;
        }

        _listSource.SetMark(index, !_listSource.IsMarked(index));
        _listSource.RaiseCollectionChanged();
        _list.SetNeedsDraw();
        UpdateBanner();

        if (index < _listSource.Count - 1)
        {
            _list.SelectedItem = index + 1;
            _list.EnsureSelectedItemVisible();
        }

        return true;
    }

    private void CyclePane(int direction)
    {
        // Focus the inner views, not their frames: a focused FrameView would swallow keys
        // before the ListView or reader inside it ever sees them.
        var panes = new List<View>();
        if (_sidebarFrame.Visible)
        {
            panes.Add(_sidebar);
        }

        if (_listFrame.Visible)
        {
            panes.Add(_list);
        }

        if (_readerFrame.Visible)
        {
            panes.Add(_reader);
        }

        if (panes.Count == 0)
        {
            return;
        }

        var current = panes.FindIndex(p => p.HasFocus);
        var next = ((current < 0 ? 0 : current + direction) % panes.Count + panes.Count) % panes.Count;
        panes[next].SetFocus();
    }

    // --------------------------------------------------------------- actions

    private void ToggleRead()
    {
        var targets = ActionTargets();
        if (targets.Count == 0)
        {
            return;
        }

        // With a mixed selection, the majority state decides so one press is decisive.
        var unreadCount = targets.Count(e => e.IsUnread);
        var newStatus = unreadCount >= targets.Count - unreadCount ? EntryStatus.Read : EntryStatus.Unread;

        MarkEntries(targets, newStatus, quiet: false);
    }

    private void MarkEntries(IReadOnlyList<Entry> entries, EntryStatus status, bool quiet)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var verb = status == EntryStatus.Read ? "read" : "unread";
        var label = entries.Count == 1 ? $"Marked {verb}" : $"Marked {entries.Count} {verb}";

        _ = RunOperationAsync($"Marking {verb}…", ct => _session.SetStatusAsync(entries, status, ct),
            onSuccess: () =>
            {
                // A quiet mark is the automatic mark-on-select, which fires on every
                // cursor move. It must not clear the marked set, or `Space Space Space s`
                // would lose its selection the moment the cursor advanced.
                AfterEntryMutation(clearMarks: !quiet);

                if (!quiet)
                {
                    _status.Show(label, ToastKind.Good);
                }
            },
            quiet: quiet);
    }

    private void ToggleStar()
    {
        var targets = ActionTargets();
        if (targets.Count == 0)
        {
            return;
        }

        _ = RunOperationAsync("Updating star…", async ct =>
        {
            foreach (var entry in targets)
            {
                await _session.ToggleStarAsync(entry, ct).ConfigureAwait(false);
            }
        }, onSuccess: () =>
        {
            AfterEntryMutation();
            var starred = targets.Count(e => e.Starred);
            _status.Show(
                targets.Count == 1
                    ? (targets[0].Starred ? "Starred" : "Unstarred")
                    : $"{starred} of {targets.Count} starred",
                ToastKind.Good);
        });
    }

    /// <summary>
    /// The headline action: push to the bookmark service configured in Miniflux. Failures
    /// are reported loudly, because a save you believe happened and did not is the one
    /// error in this app with lasting consequences.
    /// </summary>
    private void SaveToBookmarkService()
    {
        var targets = ActionTargets();
        if (targets.Count == 0)
        {
            return;
        }

        var failures = new List<string>();

        _ = RunOperationAsync(
            targets.Count == 1 ? "Saving…" : $"Saving {targets.Count} entries…",
            async ct =>
            {
                foreach (var entry in targets)
                {
                    try
                    {
                        await _session.SaveToThirdPartyAsync(entry, ct).ConfigureAwait(false);
                    }
                    catch (MinifluxException ex)
                    {
                        failures.Add($"{Ellipsize(entry.Title, 40)}: {ex.UserMessage}");
                    }
                }
            },
            onSuccess: () =>
            {
                AfterEntryMutation();

                if (failures.Count == 0)
                {
                    _status.Show(
                        targets.Count == 1 ? "Saved to bookmarks" : $"Saved {targets.Count} to bookmarks",
                        ToastKind.Good);
                    return;
                }

                var saved = targets.Count - failures.Count;
                _status.Show(
                    saved > 0
                        ? $"Saved {saved}, {failures.Count} failed — {failures[0]}"
                        : failures[0],
                    ToastKind.Bad);
            });
    }

    private void OpenInBrowser()
    {
        var entry = _paneMode == PaneMode.ReaderOnly ? _openEntry ?? SelectedEntry : SelectedEntry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Url))
        {
            _status.Show("No link for this entry", ToastKind.Bad);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.Url) { UseShellExecute = true });
            _status.Show("Opened in browser");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            _status.Show($"Could not open browser: {ex.Message}", ToastKind.Bad);
        }
    }

    private void FetchFullText(Entry? entry, bool quiet)
    {
        if (entry is null)
        {
            return;
        }

        _ = RunOperationAsync("Fetching full text…", async ct =>
        {
            var content = await _session.FetchFullTextAsync(entry, ct).ConfigureAwait(false);
            return content;
        }, onResult: content =>
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                if (!quiet)
                {
                    _status.Show("Miniflux returned no additional content", ToastKind.Bad);
                }

                return;
            }

            // Only replace what is on screen; the cached entry keeps the feed's version,
            // so moving away and back shows the original unless it is fetched again.
            if (ReferenceEquals(_openEntry, entry))
            {
                _reader.Text = ArticleRenderer.Render(entry, contentOverride: content);
                _reader.Viewport = _reader.Viewport with { Y = 0 };
                _reader.SetNeedsDraw();
            }

            if (!quiet)
            {
                _status.Show("Full text loaded", ToastKind.Good);
            }
        }, quiet: quiet);
    }

    private void MarkAllRead()
    {
        var count = _session.Entries.Count(e => e.IsUnread);
        if (count == 0)
        {
            _status.Show("Nothing unread here");
            return;
        }

        if (App is not { } app)
        {
            return;
        }

        var answer = Terminal.Gui.Views.MessageBox.Query(
            app,
            "Mark all read",
            $"Mark {count} entries in \"{CurrentViewLabel()}\" as read?",
            "Cancel", "Mark read");

        if (answer != 1)
        {
            return;
        }

        _ = RunOperationAsync("Marking all read…", ct => _session.MarkAllListedReadAsync(ct),
            onSuccess: () =>
            {
                AfterEntryMutation();
                _status.Show($"Marked {count} read", ToastKind.Good);
            });
    }

    private void ReloadCurrentView()
    {
        _ = RunOperationAsync("Refreshing…", async ct =>
        {
            await _session.LoadNavigationAsync(ct).ConfigureAwait(false);
            await _session.LoadEntriesAsync(_session.CurrentQuery with { Offset = 0 }, ct).ConfigureAwait(false);
        }, onSuccess: () =>
        {
            RebuildSidebar();
            RefreshListFromSession(selectFirst: false);
            _status.Show("Refreshed", ToastKind.Good);
        });
    }

    private void RefreshFeedsOnServer()
    {
        _ = RunOperationAsync("Asking Miniflux to poll feeds…", ct => _session.RefreshFeedsAsync(ct),
            onSuccess: () => _status.Show("Feed refresh queued on the server", ToastKind.Good));
    }

    /// <summary>
    /// Refreshes the chrome after entries changed. <paramref name="clearMarks"/> consumes
    /// the batch selection, which only an explicit action should do.
    /// </summary>
    private void AfterEntryMutation(bool clearMarks = true)
    {
        if (clearMarks)
        {
            _listSource.ClearMarks();
        }

        _list.SetNeedsDraw();
        UpdateBanner();
        UpdateListTitle();
        RebuildSidebar();
    }

    /// <summary>Opens the inline search bar and puts the cursor in it.</summary>
    private void ShowSearchBar()
    {
        _searchVisible = true;
        _searchField.Text = "";
        ApplyLayout();
        _searchField.SetFocus();
        _status.ClearToast();
    }

    private void HideSearchBar(bool returnFocusToList)
    {
        if (!_searchVisible)
        {
            return;
        }

        _searchVisible = false;
        ApplyLayout();

        if (returnFocusToList)
        {
            _list.SetFocus();
        }
    }

    private void OnSearchFieldKeyDown(object? sender, Key key)
    {
        if (key == Key.Esc)
        {
            HideSearchBar(returnFocusToList: true);
            key.Handled = true;
            return;
        }

        if (key == Key.Enter)
        {
            var term = _searchField.Text;
            HideSearchBar(returnFocusToList: true);
            RunSearch(term);
            key.Handled = true;
        }

        // Everything else falls through to the TextField so the query is visible as it
        // is typed — which a modal prompt did not manage.
    }

    private void RunSearch(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        // Remember where we were so Esc can come back to it.
        _restorePoint ??= new ViewRestorePoint(
            _sidebar.SelectedItem ?? 1,
            _session.CurrentQuery,
            _listFrame.Title);

        _ = RunOperationAsync($"Searching for \"{term}\"…", ct =>
            _session.LoadEntriesAsync(
                new EntryQuery { Search = term, Limit = _config.PageSize },
                ct),
            onSuccess: () =>
            {
                RefreshListFromSession(selectFirst: true);
                _listFrame.Title = $"Search: {term} ({_session.TotalMatching})";
                UpdateLegend();

                _status.Show(
                    _session.TotalMatching == 0
                        ? $"No results for \"{term}\" — Esc to go back"
                        : $"{_session.TotalMatching} results · Esc to go back",
                    _session.TotalMatching == 0 ? ToastKind.Bad : ToastKind.Good);
            });
    }

    /// <summary>Leaves search results and reloads whatever view was showing beforehand.</summary>
    private void LeaveSearchResults()
    {
        if (_restorePoint is not { } restore)
        {
            return;
        }

        _restorePoint = null;

        _ = RunOperationAsync("Going back…", ct =>
            _session.LoadEntriesAsync(restore.Query with { Offset = 0 }, ct),
            onSuccess: () =>
            {
                RefreshListFromSession(selectFirst: true);

                // Put the sidebar cursor back without re-triggering a load.
                if (restore.SidebarIndex < _sidebarSource.Count)
                {
                    _suppressSidebarLoad = true;
                    _sidebar.SelectedItem = restore.SidebarIndex;
                    _suppressSidebarLoad = false;
                }

                _listFrame.Title = restore.Title;
                UpdateLegend();
                _status.Show("Back to " + CurrentViewLabel());
            });
    }

    private void ShowThemePicker()
    {
        var chosen = ThemePickerDialog.Prompt(this, ThemeCatalog.CurrentTheme);
        if (chosen is null)
        {
            return;
        }

        ThemeCatalog.Apply(chosen);
        _config.Theme = chosen;

        try
        {
            ConfigStore.Save(_config);
        }
        catch (InvalidOperationException ex)
        {
            _status.Show($"Theme applied but not saved: {ex.Message}", ToastKind.Bad);
        }

        SetNeedsDraw();
        _status.Show($"Theme: {chosen}", ToastKind.Good);
    }

    // ------------------------------------------------------- async plumbing

    private Task RunOperationAsync(
        string busyMessage,
        Func<CancellationToken, Task> work,
        Action? onSuccess = null,
        bool quiet = false) =>
        RunOperationCoreAsync<object?>(busyMessage, async ct =>
        {
            await work(ct).ConfigureAwait(false);
            return null;
        }, _ => onSuccess?.Invoke(), quiet);

    private Task RunOperationAsync<T>(
        string busyMessage,
        Func<CancellationToken, Task<T>> work,
        Action<T> onResult,
        bool quiet = false) =>
        RunOperationCoreAsync(busyMessage, work, onResult, quiet);

    /// <summary>
    /// Runs server work off the UI thread and marshals the result back. Terminal.Gui is
    /// single-threaded, so every view touch has to come back through Invoke.
    /// </summary>
    private async Task RunOperationCoreAsync<T>(
        string busyMessage,
        Func<CancellationToken, Task<T>> work,
        Action<T> onResult,
        bool quiet)
    {
        if (!quiet)
        {
            _status.Show(busyMessage);
        }

        _busy = true;
        var token = _operationCts.Token;

        try
        {
            var result = await Task.Run(() => work(token), token).ConfigureAwait(false);

            OnUiThread(() =>
            {
                _busy = false;
                onResult(result);
            });
        }
        catch (OperationCanceledException)
        {
            _busy = false;
        }
        catch (MinifluxException ex)
        {
            OnUiThread(() =>
            {
                _busy = false;
                _status.Show(ex.UserMessage, ToastKind.Bad);
            });
        }
        catch (Exception ex)
        {
            OnUiThread(() =>
            {
                _busy = false;
                _status.Show($"Unexpected error: {ex.Message}", ToastKind.Bad);
            });
        }
    }

    /// <summary>
    /// Marshals back to the UI thread, or runs inline when there is no application yet.
    /// Without the fallback a result arriving before the app is running is dropped
    /// silently — which shows up as an empty list rather than an error.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (App is { } app)
        {
            app.Invoke(action);
            return;
        }

        action();
    }

    /// <summary>Cancels in-flight work. Called when the shell is closing.</summary>
    public void CancelPendingWork()
    {
        _operationCts.Cancel();
        _operationCts.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_attachedApp is { } attached)
            {
                attached.Keyboard.KeyDown -= OnAppKeyDown;
                _attachedApp = null;
            }

            ClearChord();

            if (_readerDebounce is not null)
            {
                App?.RemoveTimeout(_readerDebounce);
                _readerDebounce = null;
            }

            _operationCts.Cancel();
            _operationCts.Dispose();
        }

        base.Dispose(disposing);
    }
}
