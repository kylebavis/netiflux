using Netiflux.Core.Models;

namespace Netiflux.Core;

/// <summary>
/// The slice of the Miniflux API this app uses. Kept as an interface so the UI can be
/// driven by a fake during development and tests without a live server.
/// </summary>
public interface IMinifluxClient
{
    Task<MinifluxUser> GetMeAsync(CancellationToken ct = default);

    Task<EntryPage> GetEntriesAsync(EntryQuery query, CancellationToken ct = default);

    Task<Entry> GetEntryAsync(long entryId, CancellationToken ct = default);

    Task<IReadOnlyList<Feed>> GetFeedsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default);

    /// <summary>Per-feed read/unread counts. Feeds themselves do not carry these.</summary>
    Task<FeedCounters> GetFeedCountersAsync(CancellationToken ct = default);

    Task UpdateEntryStatusAsync(IReadOnlyList<long> entryIds, EntryStatus status, CancellationToken ct = default);

    /// <summary>Flips the starred flag. Miniflux exposes only a toggle, not an absolute set.</summary>
    Task ToggleBookmarkAsync(long entryId, CancellationToken ct = default);

    /// <summary>
    /// Pushes the entry to the integration configured in Miniflux (Wallabag, Shiori, Linkding, …).
    /// Returns 202 Accepted, so success here means "queued", not "stored".
    /// </summary>
    Task SaveToThirdPartyAsync(long entryId, CancellationToken ct = default);

    /// <summary>Asks Miniflux to re-fetch the full article text using its scraper rules.</summary>
    Task<string> FetchOriginalContentAsync(long entryId, CancellationToken ct = default);

    Task RefreshAllFeedsAsync(CancellationToken ct = default);
}
