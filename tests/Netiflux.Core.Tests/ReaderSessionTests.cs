using System.Net;
using Netiflux.Core;
using Netiflux.Core.Configuration;
using Netiflux.Core.Models;
using Netiflux.Core.State;
using Netiflux.Services;

namespace Netiflux.Core.Tests;

public class ReaderSessionTests : IDisposable
{
    private readonly string _stateDir = Path.Combine(
        Path.GetTempPath(), "netiflux-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetStatus_UpdatesEntriesAndUnreadCount()
    {
        var (session, client) = NewSession(UnreadEntries(3), categories:
        [
            new Category { Id = 1, Title = "Tech", TotalUnread = 3 }
        ]);

        await session.LoadNavigationAsync();
        await session.LoadEntriesAsync(new EntryQuery { Statuses = [EntryStatus.Unread] });

        var before = session.UnreadTotal;
        Assert.Equal(3, before);

        await session.SetStatusAsync([session.Entries[0], session.Entries[1]], EntryStatus.Read);

        Assert.All(session.Entries.Take(2), e => Assert.Equal(EntryStatus.Read, e.Status));
        Assert.Equal(EntryStatus.Unread, session.Entries[2].Status);
        Assert.Equal(before - 2, session.UnreadTotal);
        Assert.Contains("UpdateStatus([1,2],Read)", client.Calls);
    }

    [Fact]
    public async Task SetStatus_WhenTheServerRejectsIt_RollsBackTheOptimisticChange()
    {
        var (session, client) = NewSession(UnreadEntries(2));
        await session.LoadEntriesAsync(new EntryQuery());

        client.NextFailure = new MinifluxException("boom", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<MinifluxException>(
            () => session.SetStatusAsync([session.Entries[0]], EntryStatus.Read));

        Assert.Equal(EntryStatus.Unread, session.Entries[0].Status);
    }

    [Fact]
    public async Task SetStatus_SkipsEntriesAlreadyInTheTargetState()
    {
        var (session, client) = NewSession(UnreadEntries(2));
        await session.LoadEntriesAsync(new EntryQuery());

        await session.SetStatusAsync([session.Entries[0]], EntryStatus.Unread);

        Assert.DoesNotContain(client.Calls, c => c.StartsWith("UpdateStatus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ToggleStar_FlipsTheFlagAndRollsBackOnFailure()
    {
        var (session, client) = NewSession(UnreadEntries(1));
        await session.LoadEntriesAsync(new EntryQuery());
        var entry = session.Entries[0];

        await session.ToggleStarAsync(entry);
        Assert.True(entry.Starred);

        client.NextFailure = new MinifluxException("nope", HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<MinifluxException>(() => session.ToggleStarAsync(entry));

        Assert.True(entry.Starred);
    }

    [Fact]
    public async Task SaveToThirdParty_RecordsTheEntryLocallyOnlyAfterTheServerAccepts()
    {
        var (session, client) = NewSession(UnreadEntries(1));
        await session.LoadEntriesAsync(new EntryQuery());
        var entry = session.Entries[0];

        await session.SaveToThirdPartyAsync(entry);

        Assert.Contains(entry.Id, client.SavedEntryIds);
        Assert.True(session.SavedStore.IsSaved(entry.Id));
    }

    [Fact]
    public async Task SaveToThirdParty_WhenItFails_DoesNotMarkTheEntrySaved()
    {
        var (session, client) = NewSession(UnreadEntries(1));
        await session.LoadEntriesAsync(new EntryQuery());
        var entry = session.Entries[0];

        client.NextFailure = new MinifluxException("no integration", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<MinifluxException>(() => session.SaveToThirdPartyAsync(entry));

        Assert.False(session.SavedStore.IsSaved(entry.Id));
    }

    [Fact]
    public async Task LoadMore_AppendsWithoutDuplicating()
    {
        var (session, _) = NewSession(UnreadEntries(25));
        await session.LoadEntriesAsync(new EntryQuery { Statuses = [EntryStatus.Unread], Limit = 10 });

        Assert.Equal(10, session.Entries.Count);
        Assert.True(session.HasMore);

        var added = await session.LoadMoreAsync();

        Assert.Equal(10, added);
        Assert.Equal(20, session.Entries.Count);
        Assert.Equal(session.Entries.Count, session.Entries.Select(e => e.Id).Distinct().Count());

        await session.LoadMoreAsync();

        Assert.Equal(25, session.Entries.Count);
        Assert.False(session.HasMore);
        Assert.Equal(0, await session.LoadMoreAsync());
    }

    [Fact]
    public async Task MarkAllListedRead_ClearsEveryUnreadEntryInView()
    {
        var (session, _) = NewSession(UnreadEntries(5));
        await session.LoadEntriesAsync(new EntryQuery { Statuses = [EntryStatus.Unread] });

        await session.MarkAllListedReadAsync();

        Assert.All(session.Entries, e => Assert.Equal(EntryStatus.Read, e.Status));
    }

    [Fact]
    public async Task LoadNavigation_SumsUnreadAcrossCategories()
    {
        var (session, _) = NewSession(UnreadEntries(3), categories:
        [
            new Category { Id = 1, Title = "Tech", TotalUnread = 12 },
            new Category { Id = 2, Title = "News", TotalUnread = 30 }
        ]);

        await session.LoadNavigationAsync();

        Assert.Equal(42, session.UnreadTotal);
        Assert.Equal(2, session.Categories.Count);
    }

    private (ReaderSession Session, FakeMinifluxClient Client) NewSession(
        List<Entry> entries,
        List<Category>? categories = null)
    {
        Directory.CreateDirectory(_stateDir);

        var client = new FakeMinifluxClient
        {
            Entries = entries,
            Categories = categories ?? []
        };

        var store = SavedEntryStore.Load(Path.Combine(_stateDir, "saved.json"));
        var config = new NetifluxConfig { PageSize = 10 };

        return (new ReaderSession(client, config, store), client);
    }

    private static List<Entry> UnreadEntries(int count) =>
        Enumerable.Range(1, count).Select(i => new Entry
        {
            Id = i,
            FeedId = 1,
            Title = $"Entry {i}",
            Url = $"https://example.org/{i}",
            Content = "<p>body</p>",
            Status = EntryStatus.Unread,
            PublishedAt = DateTimeOffset.UtcNow.AddHours(-i),
            Feed = new Feed { Id = 1, Title = "Feed" }
        }).ToList();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_stateDir))
            {
                Directory.Delete(_stateDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }
}
