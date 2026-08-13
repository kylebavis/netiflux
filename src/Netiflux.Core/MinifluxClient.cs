using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netiflux.Core.Models;

namespace Netiflux.Core;

/// <summary>
/// HTTP implementation of <see cref="IMinifluxClient"/>. Authenticates with the
/// <c>X-Auth-Token</c> header, which Miniflux documents as the preferred scheme.
/// </summary>
public sealed class MinifluxClient : IMinifluxClient, IDisposable
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public MinifluxClient(Uri baseAddress, string apiToken, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();

        // A trailing slash matters: without it, Uri composition drops the last path segment
        // of a base address like https://host/miniflux.
        var normalized = baseAddress.AbsoluteUri.EndsWith('/')
            ? baseAddress
            : new Uri(baseAddress.AbsoluteUri + "/");

        _http.BaseAddress = normalized;
        _http.DefaultRequestHeaders.Remove("X-Auth-Token");
        _http.DefaultRequestHeaders.Add("X-Auth-Token", apiToken);

        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("Netiflux/0.1"))
        {
            // Non-fatal: some handlers pre-populate a UA we are not allowed to touch.
        }

        if (_http.Timeout == TimeSpan.FromSeconds(100))
        {
            _http.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    public Task<MinifluxUser> GetMeAsync(CancellationToken ct = default) =>
        GetAsync<MinifluxUser>("v1/me", ct);

    public Task<EntryPage> GetEntriesAsync(EntryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Per-feed listing has its own endpoint; everything else filters on /v1/entries.
        var path = query.FeedId is { } feedId
            ? $"v1/feeds/{feedId.ToString(CultureInfo.InvariantCulture)}/entries"
            : "v1/entries";

        return GetAsync<EntryPage>($"{path}?{query.ToQueryString()}", ct);
    }

    public Task<Entry> GetEntryAsync(long entryId, CancellationToken ct = default) =>
        GetAsync<Entry>($"v1/entries/{entryId.ToString(CultureInfo.InvariantCulture)}", ct);

    public Task<IReadOnlyList<Feed>> GetFeedsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<Feed>>("v1/feeds", ct);

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<Category>>("v1/categories?counts=true", ct);

    public Task<FeedCounters> GetFeedCountersAsync(CancellationToken ct = default) =>
        GetAsync<FeedCounters>("v1/feeds/counters", ct);

    public async Task UpdateEntryStatusAsync(
        IReadOnlyList<long> entryIds,
        EntryStatus status,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count == 0)
        {
            return;
        }

        var body = new StatusUpdate
        {
            EntryIds = entryIds,
            Status = status == EntryStatus.Read ? "read" : status == EntryStatus.Unread ? "unread" : "removed"
        };

        using var response = await _http
            .PutAsJsonAsync("v1/entries", body, JsonOptions, ct)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "update entry status", ct).ConfigureAwait(false);
    }

    public async Task ToggleBookmarkAsync(long entryId, CancellationToken ct = default)
    {
        using var response = await _http
            .PutAsync($"v1/entries/{entryId.ToString(CultureInfo.InvariantCulture)}/bookmark", null, ct)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "toggle bookmark", ct).ConfigureAwait(false);
    }

    public async Task SaveToThirdPartyAsync(long entryId, CancellationToken ct = default)
    {
        using var response = await _http
            .PostAsync($"v1/entries/{entryId.ToString(CultureInfo.InvariantCulture)}/save", null, ct)
            .ConfigureAwait(false);

        // Miniflux answers 202 when an integration is configured and 400 when none is.
        // The 400 case is worth calling out precisely — it is a setup problem, not a bug.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new MinifluxException(
                "No third-party integration is configured in Miniflux (Settings → Integrations).",
                HttpStatusCode.BadRequest);
        }

        await EnsureSuccessAsync(response, "save entry", ct).ConfigureAwait(false);
    }

    public async Task<string> FetchOriginalContentAsync(long entryId, CancellationToken ct = default)
    {
        var result = await GetAsync<FetchContentResult>(
            $"v1/entries/{entryId.ToString(CultureInfo.InvariantCulture)}/fetch-content",
            ct).ConfigureAwait(false);

        return result.Content ?? "";
    }

    public async Task RefreshAllFeedsAsync(CancellationToken ct = default)
    {
        using var response = await _http.PutAsync("v1/feeds/refresh", null, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "refresh feeds", ct).ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new MinifluxException($"Could not reach Miniflux: {ex.Message}", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new MinifluxException("The request to Miniflux timed out.", null, ex);
        }

        using (response)
        {
            await EnsureSuccessAsync(response, $"GET {path}", ct).ConfigureAwait(false);

            try
            {
                var value = await response.Content
                    .ReadFromJsonAsync<T>(JsonOptions, ct)
                    .ConfigureAwait(false);

                return value ?? throw new MinifluxException($"Miniflux returned an empty body for {path}.");
            }
            catch (JsonException ex)
            {
                throw new MinifluxException(
                    $"Could not parse the response from {path}: {ex.Message}", response.StatusCode, ex);
            }
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = "";
        try
        {
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                // Miniflux errors look like {"error_message":"..."}; fall back to the raw body.
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("error_message", out var message))
                    {
                        detail = message.GetString() ?? "";
                    }
                }
                catch (JsonException)
                {
                    detail = raw.Length > 200 ? raw[..200] : raw;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Body unavailable; the status code alone still tells the user enough.
        }

        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $" — {detail}";
        throw new MinifluxException(
            $"Failed to {operation}: {(int)response.StatusCode} {response.ReasonPhrase}{suffix}",
            response.StatusCode);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private sealed class StatusUpdate
    {
        public IReadOnlyList<long> EntryIds { get; init; } = [];
        public string Status { get; init; } = "read";
    }

    private sealed class FetchContentResult
    {
        public string? Content { get; init; }
    }
}
