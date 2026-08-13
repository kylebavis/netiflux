using System.Net;
using System.Text;
using Netiflux.Core;
using Netiflux.Core.Models;

namespace Netiflux.Core.Tests;

public class MinifluxClientTests
{
    [Fact]
    public async Task GetEntries_SendsAuthTokenHeader()
    {
        var handler = new RecordingHandler(EmptyEntryPage);
        using var client = NewClient(handler);

        await client.GetEntriesAsync(new EntryQuery());

        Assert.Equal("token-123", Assert.Single(handler.LastRequest!.Headers.GetValues("X-Auth-Token")));
    }

    [Fact]
    public async Task GetEntries_BuildsFilterQueryString()
    {
        var handler = new RecordingHandler(EmptyEntryPage);
        using var client = NewClient(handler);

        await client.GetEntriesAsync(new EntryQuery
        {
            Statuses = [EntryStatus.Unread, EntryStatus.Read],
            CategoryId = 7,
            Starred = true,
            Order = EntryOrder.PublishedAt,
            Direction = SortDirection.Desc,
            Limit = 50,
            Offset = 100
        });

        var query = handler.LastRequest!.RequestUri!.Query;

        Assert.Contains("status=unread", query, StringComparison.Ordinal);
        Assert.Contains("status=read", query, StringComparison.Ordinal);
        Assert.Contains("category_id=7", query, StringComparison.Ordinal);
        Assert.Contains("starred=true", query, StringComparison.Ordinal);
        Assert.Contains("order=published_at", query, StringComparison.Ordinal);
        Assert.Contains("direction=desc", query, StringComparison.Ordinal);
        Assert.Contains("limit=50", query, StringComparison.Ordinal);
        Assert.Contains("offset=100", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEntries_WithFeedId_UsesFeedScopedEndpoint()
    {
        var handler = new RecordingHandler(EmptyEntryPage);
        using var client = NewClient(handler);

        await client.GetEntriesAsync(new EntryQuery { FeedId = 42 });

        Assert.Equal("/v1/feeds/42/entries", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetEntries_ParsesSnakeCasePayload()
    {
        const string json = """
        {
          "total": 1,
          "entries": [{
            "id": 888,
            "feed_id": 42,
            "title": "Entry Title",
            "url": "http://example.org/article.html",
            "author": "Foobar",
            "content": "<p>HTML contents</p>",
            "published_at": "2016-12-12T16:15:19Z",
            "status": "unread",
            "starred": false,
            "reading_time": 5,
            "feed": { "id": 42, "title": "Example Feed" }
          }]
        }
        """;

        using var client = NewClient(new RecordingHandler(json));

        var page = await client.GetEntriesAsync(new EntryQuery());
        var entry = Assert.Single(page.Entries);

        Assert.Equal(1, page.Total);
        Assert.Equal(888, entry.Id);
        Assert.Equal("Entry Title", entry.Title);
        Assert.Equal(EntryStatus.Unread, entry.Status);
        Assert.True(entry.IsUnread);
        Assert.Equal(5, entry.ReadingTime);
        Assert.Equal("Example Feed", entry.FeedTitle);
    }

    [Fact]
    public async Task UpdateEntryStatus_PutsEntryIdsAndStatus()
    {
        var handler = new RecordingHandler("", HttpStatusCode.NoContent);
        using var client = NewClient(handler);

        await client.UpdateEntryStatusAsync([1, 2, 3], EntryStatus.Read);

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/v1/entries", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"entry_ids\":[1,2,3]", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"read\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateEntryStatus_WithNoIds_SkipsTheRequest()
    {
        var handler = new RecordingHandler("", HttpStatusCode.NoContent);
        using var client = NewClient(handler);

        await client.UpdateEntryStatusAsync([], EntryStatus.Read);

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SaveToThirdParty_PostsToSaveEndpoint()
    {
        var handler = new RecordingHandler("", HttpStatusCode.Accepted);
        using var client = NewClient(handler);

        await client.SaveToThirdPartyAsync(555);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/v1/entries/555/save", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task SaveToThirdParty_WhenNoIntegrationConfigured_ExplainsWhy()
    {
        var handler = new RecordingHandler("", HttpStatusCode.BadRequest);
        using var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<MinifluxException>(() => client.SaveToThirdPartyAsync(555));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("integration", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToggleBookmark_PutsToBookmarkEndpoint()
    {
        var handler = new RecordingHandler("", HttpStatusCode.NoContent);
        using var client = NewClient(handler);

        await client.ToggleBookmarkAsync(99);

        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/v1/entries/99/bookmark", handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Unauthorized_IsReportedAsAnAuthFailure()
    {
        var handler = new RecordingHandler(
            """{"error_message":"access unauthorized"}""",
            HttpStatusCode.Unauthorized);

        using var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<MinifluxException>(() => client.GetMeAsync());

        Assert.True(ex.IsAuthFailure);
        Assert.Contains("token", ex.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServerErrorBody_IsSurfacedInTheMessage()
    {
        var handler = new RecordingHandler(
            """{"error_message":"something broke"}""",
            HttpStatusCode.InternalServerError);

        using var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<MinifluxException>(() => client.GetFeedsAsync());

        Assert.Contains("something broke", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaseUrlWithSubPath_KeepsThatPath()
    {
        var handler = new RecordingHandler(EmptyEntryPage);
        using var http = new HttpClient(handler);
        using var client = new MinifluxClient(new Uri("https://example.org/miniflux"), "t", http);

        await client.GetEntriesAsync(new EntryQuery());

        Assert.Equal("/miniflux/v1/entries", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task NetworkFailure_BecomesAFriendlyMinifluxException()
    {
        var handler = new ThrowingHandler(new HttpRequestException("no route to host"));
        using var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<MinifluxException>(() => client.GetMeAsync());

        Assert.Contains("Could not reach Miniflux", ex.Message, StringComparison.Ordinal);
    }

    private const string EmptyEntryPage = """{"total":0,"entries":[]}""";

    private static MinifluxClient NewClient(HttpMessageHandler handler) =>
        new(new Uri("https://example.org"), "token-123", new HttpClient(handler));

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string LastBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw exception;
    }
}
