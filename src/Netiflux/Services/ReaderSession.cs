using Netiflux.Core;
using Netiflux.Core.Configuration;
using Netiflux.Core.Models;
using Netiflux.Core.State;

namespace Netiflux.Services;

/// <summary>
/// Everything the UI knows about the current session: what is loaded, what is selected,
/// and the operations the triage keys invoke.
/// <para>
/// Mutating operations update the in-memory entry first and call the server afterwards.
/// Triage is a rhythm — you hold a key down and move — and a UI that waits on a
/// round-trip before redrawing breaks that rhythm. When the call fails the change is
/// rolled back and the shell shows why.
/// </para>
/// </summary>
public sealed class ReaderSession
{
    private readonly IMinifluxClient _client;
    private readonly SavedEntryStore _savedStore;

    public ReaderSession(IMinifluxClient client, NetifluxConfig config, SavedEntryStore savedStore)
    {
        _client = client;
        Config = config;
        _savedStore = savedStore;
    }

    public NetifluxConfig Config { get; }

    public SavedEntryStore SavedStore => _savedStore;

    public IReadOnlyList<Category> Categories { get; private set; } = [];

    public IReadOnlyList<Feed> Feeds { get; private set; } = [];

    /// <summary>Unread count per feed id, from <c>/v1/feeds/counters</c>.</summary>
    public IReadOnlyDictionary<long, int> FeedUnread { get; private set; } = new Dictionary<long, int>();

    /// <summary>Entries currently listed, in display order.</summary>
    public List<Entry> Entries { get; } = [];

    /// <summary>The query that produced <see cref="Entries"/>.</summary>
    public EntryQuery CurrentQuery { get; private set; } = new() { Statuses = [EntryStatus.Unread] };

    /// <summary>Total matches on the server, which may exceed what has been paged in.</summary>
    public int TotalMatching { get; private set; }

    public bool HasMore => Entries.Count < TotalMatching;

    public int UnreadTotal { get; private set; }

    public int StarredTotal { get; private set; }

    public async Task LoadNavigationAsync(CancellationToken ct = default)
    {
        var categoriesTask = _client.GetCategoriesAsync(ct);
        var feedsTask = _client.GetFeedsAsync(ct);
        var countersTask = _client.GetFeedCountersAsync(ct);

        await Task.WhenAll(categoriesTask, feedsTask, countersTask).ConfigureAwait(false);

        Categories = await categoriesTask.ConfigureAwait(false);
        Feeds = await feedsTask.ConfigureAwait(false);
        UnreadTotal = Categories.Sum(c => c.TotalUnread);

        try
        {
            FeedUnread = (await countersTask.ConfigureAwait(false)).Unreads;
        }
        catch (MinifluxException)
        {
            // Older Miniflux versions may not expose counters; the sidebar copes by
            // simply not knowing which feeds are busy.
            FeedUnread = new Dictionary<long, int>();
        }

        // Starred has no count endpoint; ask for a single row and read the total.
        try
        {
            var starred = await _client
                .GetEntriesAsync(new EntryQuery { Starred = true, Limit = 1 }, ct)
                .ConfigureAwait(false);

            StarredTotal = starred.Total;
        }
        catch (MinifluxException)
        {
            StarredTotal = 0;
        }
    }

    public async Task LoadEntriesAsync(EntryQuery query, CancellationToken ct = default)
    {
        var page = await _client.GetEntriesAsync(query, ct).ConfigureAwait(false);

        CurrentQuery = query;
        TotalMatching = page.Total;
        Entries.Clear();
        Entries.AddRange(page.Entries);
    }

    /// <summary>Fetches the next page and appends it. Returns how many entries arrived.</summary>
    public async Task<int> LoadMoreAsync(CancellationToken ct = default)
    {
        if (!HasMore)
        {
            return 0;
        }

        var next = CurrentQuery.WithOffset(Entries.Count);
        var page = await _client.GetEntriesAsync(next, ct).ConfigureAwait(false);

        CurrentQuery = next;
        TotalMatching = page.Total;

        // Guard against duplicates if entries shifted between pages.
        var known = Entries.Select(e => e.Id).ToHashSet();
        var added = page.Entries.Where(e => known.Add(e.Id)).ToList();
        Entries.AddRange(added);

        return added.Count;
    }

    public async Task SetStatusAsync(IReadOnlyList<Entry> entries, EntryStatus status, CancellationToken ct = default)
    {
        var changed = entries.Where(e => e.Status != status).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var previous = changed.Select(e => (Entry: e, Status: e.Status)).ToList();
        foreach (var entry in changed)
        {
            entry.Status = status;
        }

        AdjustUnreadCount(previous, status);

        try
        {
            await _client
                .UpdateEntryStatusAsync(changed.Select(e => e.Id).ToList(), status, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            foreach (var (entry, oldStatus) in previous)
            {
                entry.Status = oldStatus;
            }

            RecomputeUnreadFromRollback(previous);
            throw;
        }
    }

    private void AdjustUnreadCount(List<(Entry Entry, EntryStatus Status)> previous, EntryStatus newStatus)
    {
        foreach (var (_, oldStatus) in previous)
        {
            if (oldStatus == EntryStatus.Unread && newStatus != EntryStatus.Unread)
            {
                UnreadTotal = Math.Max(0, UnreadTotal - 1);
            }
            else if (oldStatus != EntryStatus.Unread && newStatus == EntryStatus.Unread)
            {
                UnreadTotal++;
            }
        }
    }

    private void RecomputeUnreadFromRollback(List<(Entry Entry, EntryStatus Status)> previous)
    {
        foreach (var (entry, oldStatus) in previous)
        {
            if (oldStatus == EntryStatus.Unread && entry.Status == EntryStatus.Unread)
            {
                UnreadTotal++;
            }
        }
    }

    public async Task ToggleStarAsync(Entry entry, CancellationToken ct = default)
    {
        entry.Starred = !entry.Starred;
        StarredTotal = Math.Max(0, StarredTotal + (entry.Starred ? 1 : -1));

        try
        {
            await _client.ToggleBookmarkAsync(entry.Id, ct).ConfigureAwait(false);
        }
        catch
        {
            entry.Starred = !entry.Starred;
            StarredTotal = Math.Max(0, StarredTotal + (entry.Starred ? 1 : -1));
            throw;
        }
    }

    /// <summary>
    /// Pushes an entry to the configured integration. Unlike the other mutations this one
    /// is confirmed before the local marker is set: a save that silently failed would be
    /// worse than a slightly slower key, because you would never revisit the article.
    /// </summary>
    public async Task SaveToThirdPartyAsync(Entry entry, CancellationToken ct = default)
    {
        await _client.SaveToThirdPartyAsync(entry.Id, ct).ConfigureAwait(false);

        if (Config.TrackSavedLocally)
        {
            _savedStore.MarkSaved(entry.Id);
            _savedStore.Flush();
        }
    }

    public Task<string> FetchFullTextAsync(Entry entry, CancellationToken ct = default) =>
        _client.FetchOriginalContentAsync(entry.Id, ct);

    public Task RefreshFeedsAsync(CancellationToken ct = default) =>
        _client.RefreshAllFeedsAsync(ct);

    /// <summary>Marks everything currently listed as read, in server-friendly batches.</summary>
    public async Task MarkAllListedReadAsync(CancellationToken ct = default)
    {
        const int batchSize = 200;
        var unread = Entries.Where(e => e.IsUnread).ToList();

        foreach (var batch in unread.Chunk(batchSize))
        {
            await SetStatusAsync(batch, EntryStatus.Read, ct).ConfigureAwait(false);
        }
    }
}
