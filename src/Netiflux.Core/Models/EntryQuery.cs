using System.Globalization;

namespace Netiflux.Core.Models;

/// <summary>
/// Filter/paging options for <c>GET /v1/entries</c>. Feed scoping is expressed here but the
/// client routes it to <c>/v1/feeds/{id}/entries</c>, which is the endpoint Miniflux
/// guarantees for per-feed queries. Results are always newest first.
/// </summary>
public sealed record EntryQuery
{
    /// <summary>Statuses to include. Repeated as multiple <c>status=</c> params. Empty means "no filter".</summary>
    public IReadOnlyList<EntryStatus> Statuses { get; init; } = [];

    public long? CategoryId { get; init; }
    public long? FeedId { get; init; }
    public bool? Starred { get; init; }
    public string? Search { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; }

    /// <summary>Only entries published after this instant.</summary>
    public DateTimeOffset? PublishedAfter { get; init; }

    internal string ToQueryString()
    {
        var parts = Statuses.Select(s => "status=" + s.ToString().ToLowerInvariant()).ToList();

        if (CategoryId is { } categoryId)
        {
            parts.Add("category_id=" + categoryId.ToString(CultureInfo.InvariantCulture));
        }

        if (Starred is { } starred)
        {
            parts.Add("starred=" + (starred ? "true" : "false"));
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            parts.Add("search=" + Uri.EscapeDataString(Search));
        }

        if (PublishedAfter is { } publishedAfter)
        {
            parts.Add("published_after=" + publishedAfter.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        parts.Add("order=published_at&direction=desc");
        parts.Add("limit=" + Limit.ToString(CultureInfo.InvariantCulture));

        if (Offset > 0)
        {
            parts.Add("offset=" + Offset.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join("&", parts);
    }
}
