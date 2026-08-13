using Netiflux.Core;
using Netiflux.Core.Models;

namespace Netiflux.Ui.Tests;

/// <summary>In-memory Miniflux, recording what the UI asked it to do.</summary>
public sealed class FakeMinifluxClient(List<Entry> entries) : IMinifluxClient
{
    public List<Entry> Entries { get; } = entries;

    public List<long> Saved { get; } = [];

    public List<long> Bookmarked { get; } = [];

    public List<(IReadOnlyList<long> Ids, EntryStatus Status)> StatusUpdates { get; } = [];

    public int RefreshCount { get; private set; }

    /// <summary>Armed to make the next mutating call fail, for rollback tests.</summary>
    public Exception? NextFailure { get; set; }

    public Task<MinifluxUser> GetMeAsync(CancellationToken ct = default) =>
        Task.FromResult(new MinifluxUser { Id = 1, Username = "tester" });

    public Task<EntryPage> GetEntriesAsync(EntryQuery query, CancellationToken ct = default)
    {
        ThrowIfArmed();

        IEnumerable<Entry> match = Entries;

        if (query.Statuses.Count > 0)
        {
            match = match.Where(e => query.Statuses.Contains(e.Status));
        }

        if (query.Starred is { } starred)
        {
            match = match.Where(e => e.Starred == starred);
        }

        if (query.FeedId is { } feedId)
        {
            match = match.Where(e => e.FeedId == feedId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            match = match.Where(e => e.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
        }

        var all = match.ToList();

        return Task.FromResult(new EntryPage
        {
            Total = all.Count,
            Entries = all.Skip(query.Offset).Take(query.Limit).ToList()
        });
    }

    public Task<Entry> GetEntryAsync(long entryId, CancellationToken ct = default) =>
        Task.FromResult(Entries.First(e => e.Id == entryId));

    public Task<IReadOnlyList<Feed>> GetFeedsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Feed>>([new Feed { Id = 1, Title = "Example Feed" }]);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Category>>(
        [
            new Category { Id = 1, Title = "Tech", TotalUnread = Entries.Count(e => e.IsUnread), FeedCount = 1 }
        ]);

    public Task<FeedCounters> GetFeedCountersAsync(CancellationToken ct = default) =>
        Task.FromResult(new FeedCounters
        {
            Unreads = new Dictionary<long, int> { [1] = Entries.Count(e => e.IsUnread) }
        });

    public Task UpdateEntryStatusAsync(
        IReadOnlyList<long> entryIds,
        EntryStatus status,
        CancellationToken ct = default)
    {
        ThrowIfArmed();
        StatusUpdates.Add((entryIds, status));
        return Task.CompletedTask;
    }

    public Task ToggleBookmarkAsync(long entryId, CancellationToken ct = default)
    {
        ThrowIfArmed();
        Bookmarked.Add(entryId);
        return Task.CompletedTask;
    }

    public Task SaveToThirdPartyAsync(long entryId, CancellationToken ct = default)
    {
        ThrowIfArmed();
        Saved.Add(entryId);
        return Task.CompletedTask;
    }

    public Task<string> FetchOriginalContentAsync(long entryId, CancellationToken ct = default)
    {
        ThrowIfArmed();
        return Task.FromResult("<p>Full scraped text.</p>");
    }

    public Task RefreshAllFeedsAsync(CancellationToken ct = default)
    {
        ThrowIfArmed();
        RefreshCount++;
        return Task.CompletedTask;
    }

    private void ThrowIfArmed()
    {
        if (NextFailure is null)
        {
            return;
        }

        var failure = NextFailure;
        NextFailure = null;
        throw failure;
    }
}
