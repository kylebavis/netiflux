using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Netiflux.Core.Models;

namespace Netiflux.Core.Text;

/// <summary>
/// Turns a Miniflux entry's HTML into the Markdown the reader pane renders.
/// </summary>
public static partial class ArticleRenderer
{
    private static readonly ReverseMarkdown.Converter Converter = CreateConverter();

    private static ReverseMarkdown.Converter CreateConverter()
    {
        var config = new ReverseMarkdown.Config { GithubFlavored = true };

        // Keep the text of tags we do not understand rather than dropping content.
        config.Tags.Unknown = ReverseMarkdown.Config.UnknownTagsOption.Bypass;
        config.Formatting.RemoveComments = true;
        config.Formatting.CleanupSpaces = true;
        config.Links.SmartHref = true;

        return new ReverseMarkdown.Converter(config);
    }

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLines { get; }

    [GeneratedRegex(@"^[ \t]+$", RegexOptions.Multiline)]
    private static partial Regex WhitespaceOnlyLines { get; }

    // Image handling needs three patterns because real feeds (Substack in particular)
    // emit images wrapped in links, and alt text that itself contains a Markdown link.
    // Left alone, a single hero image can dump 400 characters of CDN URL into the reader.

    /// <summary>Alt text, tolerating one level of nested brackets.</summary>
    private const string AltPattern = @"(?:[^\[\]]|\[[^\]]*\])*";

    /// <summary>An image wrapped in a link: <c>[![alt](img)](href)</c>.</summary>
    [GeneratedRegex($@"\[!\[(?<alt>{AltPattern})\]\([^)]*\)\]\([^)]*\)")]
    private static partial Regex LinkedImage { get; }

    /// <summary>A plain image anywhere in the text, not only on its own line.</summary>
    [GeneratedRegex($@"!\[(?<alt>{AltPattern})\]\([^)]*\)")]
    private static partial Regex InlineImage { get; }

    /// <summary>A Markdown link, used to reduce link syntax inside alt text to its label.</summary>
    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink { get; }

    /// <summary>
    /// Renders the full reading view: a metadata header followed by the article body.
    /// </summary>
    /// <param name="entry">The entry supplying the title, byline and default body.</param>
    /// <param name="options">Rendering options; defaults are used when null.</param>
    /// <param name="contentOverride">
    /// HTML to render instead of <see cref="Entry.Content"/>, used after a full-text fetch.
    /// </param>
    public static string Render(Entry entry, ArticleRenderOptions? options = null, string? contentOverride = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        options ??= ArticleRenderOptions.Default;

        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(EscapeHeading(entry.Title));
        sb.AppendLine();
        sb.AppendLine(BuildByline(entry));
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(entry.Url))
        {
            sb.Append("<").Append(entry.Url).AppendLine(">");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(RenderBody(contentOverride ?? entry.Content, options));

        if (entry.Enclosures is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("**Attachments**");
            sb.AppendLine();
            foreach (var enclosure in entry.Enclosures)
            {
                var label = string.IsNullOrWhiteSpace(enclosure.MimeType) ? "file" : enclosure.MimeType;
                sb.Append("- ").Append(label).Append(": <").Append(enclosure.Url).AppendLine(">");
            }
        }

        return sb.ToString();
    }

    /// <summary>Converts an HTML fragment to Markdown, without the metadata header.</summary>
    public static string RenderBody(string? html, ArticleRenderOptions? options = null)
    {
        options ??= ArticleRenderOptions.Default;

        if (string.IsNullOrWhiteSpace(html))
        {
            return "*This entry has no content. Press `v` to open the original in a browser.*";
        }

        string markdown;
        try
        {
            markdown = Converter.Convert(html);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Malformed markup should degrade to readable plain text, not crash the reader.
            return StripTags(html);
        }

        markdown = WebUtility.HtmlDecode(markdown);

        if (!options.ShowImages)
        {
            markdown = ReplaceImages(markdown);
        }

        markdown = WhitespaceOnlyLines.Replace(markdown, "");
        markdown = ExcessBlankLines.Replace(markdown, "\n\n");

        return markdown.Trim();
    }

    /// <summary>
    /// Swaps images for their alt text, or a marker when there is none. Terminals cannot
    /// show the picture, and the URL is pure noise in a reading pane — but alt text is
    /// often a real caption worth keeping.
    /// </summary>
    private static string ReplaceImages(string markdown)
    {
        // Linked images first: the outer link would otherwise survive as empty syntax.
        markdown = LinkedImage.Replace(markdown, match => Describe(match.Groups["alt"].Value));

        return InlineImage.Replace(markdown, match => Describe(match.Groups["alt"].Value));
    }

    private static string Describe(string altText)
    {
        // Alt text sometimes contains a whole Markdown link; keep only its label.
        var alt = MarkdownLink.Replace(altText ?? "", "${text}").Trim();

        return string.IsNullOrWhiteSpace(alt) ? "*[image]*" : $"*[image: {alt}]*";
    }

    private static string BuildByline(Entry entry)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.FeedTitle))
        {
            parts.Add(entry.FeedTitle);
        }

        if (!string.IsNullOrWhiteSpace(entry.Author) && !string.Equals(entry.Author, entry.FeedTitle, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(entry.Author);
        }

        parts.Add(entry.PublishedAt.ToLocalTime().ToString("ddd d MMM yyyy, HH:mm", CultureInfo.CurrentCulture));

        if (entry.ReadingTime > 0)
        {
            parts.Add($"{entry.ReadingTime.ToString(CultureInfo.CurrentCulture)} min read");
        }

        return "*" + string.Join(" · ", parts) + "*";
    }

    /// <summary>Headings starting with a Markdown control character would render wrong.</summary>
    private static string EscapeHeading(string title)
    {
        var trimmed = (title ?? "").Trim();
        return string.IsNullOrEmpty(trimmed) ? "(untitled)" : trimmed.Replace("\n", " ");
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag { get; }

    private static string StripTags(string html)
    {
        var text = HtmlTag.Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = ExcessBlankLines.Replace(text, "\n\n");
        return text.Trim();
    }

    /// <summary>
    /// Heuristic for feeds that publish only a teaser. Used to offer (or trigger) a
    /// full-text fetch rather than making the user leave the app for a two-line stub.
    /// </summary>
    public static bool LooksTruncated(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.Content))
        {
            return true;
        }

        var text = StripTags(entry.Content);
        if (text.Length > 1500)
        {
            return false;
        }

        return text.Length < 400
               || text.Contains("Read more", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Continue reading", StringComparison.OrdinalIgnoreCase)
               || text.TrimEnd().EndsWith('…')
               || text.TrimEnd().EndsWith("[...]", StringComparison.Ordinal);
    }
}

public sealed record ArticleRenderOptions
{
    public static readonly ArticleRenderOptions Default = new();

    /// <summary>Keep image links in the output. Terminals mostly cannot show them, so alt text wins.</summary>
    public bool ShowImages { get; init; }
}
