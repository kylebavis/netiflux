using Netiflux.Core.Models;
using Netiflux.Core.Text;

namespace Netiflux.Core.Tests;

public class ArticleRendererTests
{
    [Fact]
    public void RenderBody_ConvertsCommonHtmlToMarkdown()
    {
        var markdown = ArticleRenderer.RenderBody(
            "<p>Hello <strong>world</strong> and <em>friends</em>.</p><h2>Section</h2><p>More.</p>");

        Assert.Contains("**world**", markdown, StringComparison.Ordinal);
        Assert.Contains("*friends*", markdown, StringComparison.Ordinal);
        Assert.Contains("## Section", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_PreservesListsAndLinks()
    {
        var markdown = ArticleRenderer.RenderBody(
            """<ul><li>one</li><li>two</li></ul><p><a href="https://example.org">link</a></p>""");

        Assert.Contains("one", markdown, StringComparison.Ordinal);
        Assert.Contains("two", markdown, StringComparison.Ordinal);
        Assert.Contains("https://example.org", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_ReplacesImagesWithAPlaceholderByDefault()
    {
        var markdown = ArticleRenderer.RenderBody("""<p><img src="https://example.org/x.png" alt=""></p>""");

        Assert.DoesNotContain("x.png", markdown, StringComparison.Ordinal);
        Assert.Contains("image", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderBody_KeepsAltTextWhenTheImageHasACaption()
    {
        var markdown = ArticleRenderer.RenderBody(
            """<p><img src="https://example.org/x.png" alt="A chart of latency over time"></p>""");

        Assert.Contains("A chart of latency over time", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("x.png", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_StripsLinkedImages()
    {
        // Substack wraps hero images in a link to the full-size asset. Left alone this
        // renders as several hundred characters of CDN URL at the top of the article.
        const string html = """
            <p><a href="https://cdn.example.org/fetch/$s_!5Vs4!,f_auto,q_auto:good/image.png">
            <img src="https://cdn.example.org/fetch/$s_!5Vs4!,w_1456,c_limit,f_auto/image.png"></a></p>
            """;

        var markdown = ArticleRenderer.RenderBody(html);

        Assert.DoesNotContain("cdn.example.org", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("q_auto", markdown, StringComparison.Ordinal);
        Assert.Contains("image", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderBody_HandlesAltTextContainingALink()
    {
        // Seen in the wild: an <img> whose alt attribute is itself markup.
        var markdown = ArticleRenderer.RenderBody(
            """<p><img src="https://example.org/x.png" alt="[preorder the book now](https://books.example.org/x)"></p>""");

        Assert.DoesNotContain("x.png", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("books.example.org", markdown, StringComparison.Ordinal);
        Assert.Contains("preorder the book now", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_ReplacesImagesMidParagraphNotJustOnTheirOwnLine()
    {
        var markdown = ArticleRenderer.RenderBody(
            """<p>Before <img src="https://example.org/inline.png" alt=""> after.</p>""");

        Assert.DoesNotContain("inline.png", markdown, StringComparison.Ordinal);
        Assert.Contains("Before", markdown, StringComparison.Ordinal);
        Assert.Contains("after.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_LeavesOrdinaryLinksAlone()
    {
        var markdown = ArticleRenderer.RenderBody(
            """<p>See <a href="https://example.org/post">this post</a>.</p>""");

        Assert.Contains("this post", markdown, StringComparison.Ordinal);
        Assert.Contains("https://example.org/post", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_WithEmptyContent_ExplainsAndSuggestsTheBrowser()
    {
        var markdown = ArticleRenderer.RenderBody("");

        Assert.Contains("no content", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderBody_CollapsesRunsOfBlankLines()
    {
        var markdown = ArticleRenderer.RenderBody("<p>a</p><br><br><br><br><p>b</p>");

        Assert.DoesNotContain("\n\n\n", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderBody_WithMalformedHtml_StillReturnsReadableText()
    {
        var markdown = ArticleRenderer.RenderBody("<p>unclosed <b>bold <i>nested</p>");

        Assert.Contains("unclosed", markdown, StringComparison.Ordinal);
        Assert.Contains("nested", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PutsTitleBylineAndLinkAboveTheBody()
    {
        var entry = NewEntry(content: "<p>Body text here.</p>");

        var markdown = ArticleRenderer.Render(entry);

        Assert.StartsWith("# Test Article", markdown, StringComparison.Ordinal);
        Assert.Contains("Example Feed", markdown, StringComparison.Ordinal);
        Assert.Contains("Jane Doe", markdown, StringComparison.Ordinal);
        Assert.Contains("5 min read", markdown, StringComparison.Ordinal);
        Assert.Contains("https://example.org/article", markdown, StringComparison.Ordinal);
        Assert.Contains("Body text here.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithContentOverride_UsesTheOverrideNotTheFeedCopy()
    {
        var entry = NewEntry(content: "<p>Truncated teaser…</p>");

        var markdown = ArticleRenderer.Render(entry, contentOverride: "<p>The full scraped article.</p>");

        Assert.Contains("The full scraped article.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Truncated teaser", markdown, StringComparison.Ordinal);
        Assert.Contains("# Test Article", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ListsEnclosures()
    {
        var entry = NewEntry(
            content: "<p>x</p>",
            enclosures: [new Enclosure { Url = "https://example.org/a.mp3", MimeType = "audio/mpeg" }]);

        var markdown = ArticleRenderer.Render(entry);

        Assert.Contains("Attachments", markdown, StringComparison.Ordinal);
        Assert.Contains("a.mp3", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithEmptyTitle_FallsBackRatherThanRenderingABareHash()
    {
        var entry = NewEntry(content: "<p>x</p>", title: "   ");

        Assert.Contains("(untitled)", ArticleRenderer.Render(entry), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("<p>short teaser</p>", true)]
    [InlineData("<p>Some intro text that runs on a while but ends with Read more</p>", true)]
    public void LooksTruncated_DetectsTeasers(string content, bool expected)
    {
        Assert.Equal(expected, ArticleRenderer.LooksTruncated(NewEntry(content: content)));
    }

    [Fact]
    public void LooksTruncated_IsFalseForAFullArticle()
    {
        var body = "<p>" + string.Concat(Enumerable.Repeat("This is a full paragraph of article text. ", 60)) + "</p>";

        Assert.False(ArticleRenderer.LooksTruncated(NewEntry(content: body)));
    }

    private static Entry NewEntry(
        string content,
        string title = "Test Article",
        IReadOnlyList<Enclosure>? enclosures = null) => new()
    {
        Id = 1,
        Title = title,
        Url = "https://example.org/article",
        Author = "Jane Doe",
        Content = content,
        ReadingTime = 5,
        PublishedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero),
        Feed = new Feed { Id = 42, Title = "Example Feed" },
        Enclosures = enclosures
    };
}
