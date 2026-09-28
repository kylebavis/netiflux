using System.Text.Json.Serialization;

namespace Netiflux.Core.Models;

/// <summary>Read state of an entry, as understood by Miniflux.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EntryStatus>))]
public enum EntryStatus
{
    [JsonStringEnumMemberName("unread")] Unread,
    [JsonStringEnumMemberName("read")] Read,
    [JsonStringEnumMemberName("removed")] Removed
}

/// <summary>
/// A single feed item. Mutable where the UI applies optimistic updates: the triage
/// keys flip <see cref="Status"/> and <see cref="Starred"/> locally before (or instead of)
/// waiting on the server round-trip.
/// </summary>
public sealed class Entry
{
    public long Id { get; init; }
    public long FeedId { get; init; }
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string Author { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTimeOffset PublishedAt { get; init; }
    public EntryStatus Status { get; set; }
    public bool Starred { get; set; }

    /// <summary>Server-estimated read time in minutes. 0 when Miniflux could not estimate one.</summary>
    public int ReadingTime { get; init; }

    public IReadOnlyList<Enclosure>? Enclosures { get; init; }
    public Feed? Feed { get; init; }

    [JsonIgnore]
    public string FeedTitle => Feed?.Title ?? "";

    [JsonIgnore]
    public bool IsUnread => Status == EntryStatus.Unread;
}

public sealed class Enclosure
{
    public string Url { get; init; } = "";
    public string MimeType { get; init; } = "";
}

public sealed class Feed
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public int ParsingErrorCount { get; init; }
}

public sealed class Category
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public int FeedCount { get; init; }
    public int TotalUnread { get; init; }
}

public sealed class MinifluxUser
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
}

/// <summary>Result page from <c>GET /v1/entries</c>. <see cref="Total"/> is the unpaged match count.</summary>
public sealed class EntryPage
{
    public int Total { get; init; }
    public IReadOnlyList<Entry> Entries { get; init; } = [];
}

/// <summary>
/// Per-feed unread tallies from <c>GET /v1/feeds/counters</c>, keyed by feed id.
/// The feed objects themselves carry no counts, so this is the only way to know which
/// feeds actually need attention.
/// </summary>
public sealed class FeedCounters
{
    public IReadOnlyDictionary<long, int> Unreads { get; init; } = new Dictionary<long, int>();
}
