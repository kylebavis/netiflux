using System.Text.Json.Serialization;

namespace Netiflux.Core.Configuration;

/// <summary>When an entry gets flipped to "read" during triage.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutoMarkRead>))]
public enum AutoMarkRead
{
    /// <summary>Never automatically; only the explicit <c>m</c> key marks entries read.</summary>
    [JsonStringEnumMemberName("never")] Never,

    /// <summary>
    /// As soon as the cursor lands on the entry in the list, without opening it. The
    /// default: moving through the list is itself the act of reading during triage.
    /// </summary>
    [JsonStringEnumMemberName("on-select")] OnSelect,

    /// <summary>As soon as the entry is opened in the reader.</summary>
    [JsonStringEnumMemberName("on-open")] OnOpen,

    /// <summary>Once the reader has been scrolled to the bottom of the article.</summary>
    [JsonStringEnumMemberName("on-scroll-end")] OnScrollEnd
}

/// <summary>User-facing settings, persisted as JSON next to the app's other state.</summary>
public sealed class NetifluxConfig
{
    /// <summary>Base URL of the Miniflux instance, e.g. <c>https://reader.example.com</c>.</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>
    /// API token in plain text. Left empty when the token comes from the environment or
    /// <see cref="ApiTokenCommand"/>, which are both preferable.
    /// </summary>
    public string? ApiToken { get; set; }

    /// <summary>
    /// Shell command whose stdout is the API token (e.g. <c>op read op://vault/miniflux/token</c>).
    /// Keeps the secret out of the config file entirely.
    /// </summary>
    public string? ApiTokenCommand { get; set; }

    public string Theme { get; set; } = "Netiflux Dark";

    public AutoMarkRead AutoMarkRead { get; set; } = AutoMarkRead.OnSelect;

    /// <summary>Entries fetched per page.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Show the feeds/categories sidebar on startup.</summary>
    public bool ShowSidebar { get; set; } = true;

    /// <summary>Sidebar width in columns.</summary>
    public int SidebarWidth { get; set; } = 28;

    /// <summary>Entry list width as a percentage of the area right of the sidebar.</summary>
    public int ListWidthPercent { get; set; } = 38;

    /// <summary>
    /// Wrap article text at this column even when the pane is wider. Long measure is
    /// tiring to read; 0 disables the cap.
    /// </summary>
    public int ReaderMaxWidth { get; set; } = 88;

    /// <summary>Ask Miniflux to scrape full text when an entry's content looks truncated.</summary>
    public bool AutoFetchTruncated { get; set; }

    /// <summary>Minutes between background unread-count refreshes. 0 disables polling.</summary>
    public int RefreshIntervalMinutes { get; set; } = 15;

    /// <summary>Remember which entries were pushed to the bookmark service (the API cannot tell us).</summary>
    public bool TrackSavedLocally { get; set; } = true;

    public static NetifluxConfig CreateDefault() => new();
}
