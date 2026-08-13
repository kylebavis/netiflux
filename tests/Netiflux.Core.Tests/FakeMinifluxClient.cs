using Netiflux.Core;
using Netiflux.Core.Models;

namespace Netiflux.Core.Tests;

/// <summary>In-memory stand-in for the API, so session behaviour can be tested without a server.</summary>
public sealed class FakeMinifluxClient : IMinifluxClient
{
    public List<Entry> Entries { get; init; } = [];

    public List<Category> Categories { get; init; } = [];

    public List<Feed> Feeds { get; init; } = [];

    /// <summary>Set to make the next mutating call throw, exercising rollback paths.</summary>
    public Exception? NextFailure { get; set; }

    public List<string> Calls { get; } = [];

    public List<long> SavedEntryIds { get; } = [];

    public string FullTextResult { get; set; } = "<p>full text</p>";

    public Task<MinifluxUser> GetMeAsync(CancellationToken ct = default) =>
        Task.FromResult(new MinifluxUser { Id = 1, Username = "tester" });

    public Task<EntryPage> GetEntriesAsync(EntryQuery query, CancellationToken ct = default)
    {
        Calls.Add($"GetEntries(offset={query.Offset},limit={query.Limit})");
        ThrowIfArmed();

        IEnumerable<Entry> matching = Entries;

        if (query.Statuses.Count > 0)
        {
            matching = matching.Where(e => query.Statuses.Contains(e.Status));
        }

        if (query.Starred is { } starred)
        {
            matching = matching.Where(e => e.Starred == starred);
        }

        if (query.FeedId is { } feedId)
        {
            matching = matching.Where(e => e.FeedId == feedId);
        }

        var all = matching.ToList();

        return Task.FromResult(new EntryPage
        {
            Total = all.Count,
            Entries = all.Skip(query.Offset).Take(query.Limit).ToList()
        });
    }

    public Task<Entry> GetEntryAsync(long entryId, CancellationToken ct = default) =>
        Task.FromResult(Entries.First(e => e.Id == entryId));

    public Task<IReadOnlyList<Feed>> GetFeedsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Feed>>(Feeds);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Category>>(Categories);

    /// <summary>Unread-per-feed counts returned by <c>GetFeedCountersAsync</c>.</summary>
    public Dictionary<long, int> FeedUnread { get; init; } = [];

    public Task<FeedCounters> GetFeedCountersAsync(CancellationToken ct = default)
    {
        Calls.Add("GetFeedCounters");
        ThrowIfArmed();
        return Task.FromResult(new FeedCounters { Unreads = FeedUnread });
    }

    public Task UpdateEntryStatusAsync(
        IReadOnlyList<long> entryIds,
        EntryStatus status,
        CancellationToken ct = default)
    {
        Calls.Add($"UpdateStatus([{string.Join(",", entryIds)}],{status})");
        ThrowIfArmed();
        return Task.CompletedTask;
    }

    public Task ToggleBookmarkAsync(long entryId, CancellationToken ct = default)
    {
        Calls.Add($"ToggleBookmark({entryId})");
        ThrowIfArmed();
        return Task.CompletedTask;
    }

    public Task SaveToThirdPartyAsync(long entryId, CancellationToken ct = default)
    {
        Calls.Add($"Save({entryId})");
        ThrowIfArmed();
        SavedEntryIds.Add(entryId);
        return Task.CompletedTask;
    }

    public Task<string> FetchOriginalContentAsync(long entryId, CancellationToken ct = default)
    {
        Calls.Add($"FetchContent({entryId})");
        ThrowIfArmed();
        return Task.FromResult(FullTextResult);
    }

    public Task RefreshAllFeedsAsync(CancellationToken ct = default)
    {
        Calls.Add("RefreshAll");
        ThrowIfArmed();
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
