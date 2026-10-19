using NmTrendIngestion.Agent;
using Xunit;

namespace NmTrendIngestion.Tests;

public class BingCitationFormatterTests
{
    private static readonly UrlCitation KrqeCitation =
        new("【3:0†source】", "https://www.krqe.com/roads-funding", "KRQE roads funding");

    [Fact]
    public void ResolveMarkdown_ReplacesMarkerWithMarkdownLink()
    {
        var result = BingCitationFormatter.ResolveMarkdown(
            "NM approved road funding【3:0†source】.", new[] { KrqeCitation });

        Assert.Equal("NM approved road funding[KRQE roads funding](https://www.krqe.com/roads-funding).", result);
    }

    [Fact]
    public void ResolveHtml_ReplacesMarkerWithEncodedAnchor()
    {
        var citation = new UrlCitation("【1:0†source】", "https://example.com/a?x=1&y=2", "A <b> & B");

        var result = BingCitationFormatter.ResolveHtml("<p>Story【1:0†source】</p>", new[] { citation });

        Assert.Equal("<p>Story<a href=\"https://example.com/a?x=1&amp;y=2\">A &lt;b&gt; &amp; B</a></p>", result);
    }

    [Fact]
    public void Resolve_FallsBackToHostWhenTitleMissing()
    {
        var citation = new UrlCitation("【2:0†source】", "https://www.kob.com/story", null);

        var result = BingCitationFormatter.ResolveMarkdown("Story【2:0†source】", new[] { citation });

        Assert.Equal("Story[www.kob.com](https://www.kob.com/story)", result);
    }

    [Fact]
    public void Resolve_RemovesUnmatchedAndNonWebMarkers()
    {
        var unsafeCitation = new UrlCitation("【5:0†source】", "javascript:alert(1)", "bad");

        var result = BingCitationFormatter.ResolveHtml(
            "One【4:0†source】 two【5:0†source】", new[] { unsafeCitation });

        Assert.Equal("One two", result);
    }

    [Fact]
    public void ToBingSearchQuery_ConvertsApiRequestUrlToBingDotCom()
    {
        var query = BingCitationFormatter.ToBingSearchQuery(
            "https://api.bing.microsoft.com/v7.0/search?q=New%20Mexico%20road%20funding&count=5");

        Assert.NotNull(query);
        Assert.Equal("New Mexico road funding", query!.Query);
        Assert.Equal("https://www.bing.com/search?q=New%20Mexico%20road%20funding", query.Url);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://api.bing.microsoft.com/v7.0/search?count=5")]
    public void ToBingSearchQuery_ReturnsNullWhenNoQuery(string requestUrl)
    {
        Assert.Null(BingCitationFormatter.ToBingSearchQuery(requestUrl));
    }

    [Fact]
    public void AppendSearchQueries_AddsLinksToBothFormats()
    {
        var queries = new[] { new BingSearchQuery("NM budget", "https://www.bing.com/search?q=NM%20budget") };

        var markdown = BingCitationFormatter.AppendSearchQueriesMarkdown("Digest", queries);
        var html = BingCitationFormatter.AppendSearchQueriesHtml("<p>Digest</p>", queries);

        Assert.Equal("Digest\n\n**Searched on Bing:** [NM budget](https://www.bing.com/search?q=NM%20budget)", markdown);
        Assert.Equal(
            "<p>Digest</p>\n<p><strong>Searched on Bing:</strong> <a href=\"https://www.bing.com/search?q=NM%20budget\">NM budget</a></p>",
            html);
    }

    [Fact]
    public void AppendSearchQueries_LeavesTextUnchangedWhenNoQueries()
    {
        var empty = Array.Empty<BingSearchQuery>();

        Assert.Equal("Digest", BingCitationFormatter.AppendSearchQueriesMarkdown("Digest", empty));
        Assert.Equal("<p>Digest</p>", BingCitationFormatter.AppendSearchQueriesHtml("<p>Digest</p>", empty));
    }

    [Fact]
    public void Resolve_ListsCitationsWithoutMarkerAsSources()
    {
        var citations = new[]
        {
            new UrlCitation(string.Empty, "https://www.kob.com/story", "KOB story"),
            new UrlCitation(string.Empty, "https://www.kob.com/story", "KOB story"),
            new UrlCitation(string.Empty, "javascript:alert(1)", "bad")
        };

        var markdown = BingCitationFormatter.ResolveMarkdown("Digest", citations);
        var html = BingCitationFormatter.ResolveHtml("<p>Digest</p>", citations);

        Assert.Equal("Digest\n\n**Sources:** [KOB story](https://www.kob.com/story)", markdown);
        Assert.Equal(
            "<p>Digest</p>\n<p><strong>Sources:</strong> <a href=\"https://www.kob.com/story\">KOB story</a></p>",
            html);
    }

    [Theory]
    [InlineData("【3:0†source】", true)]
    [InlineData("see 【3:0†source】", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCitationMarker_MatchesOnlyWholeMarkers(string? text, bool expected)
    {
        Assert.Equal(expected, BingCitationFormatter.IsCitationMarker(text));
    }

    [Theory]
    [InlineData("{\"requesturl\":\"https://api.bing.microsoft.com/v7.0/search?q=NM\",\"response_metadata\":\"x\"}", "https://api.bing.microsoft.com/v7.0/search?q=NM")]
    [InlineData("{\"request_url\":\"https://api.bing.microsoft.com/v7.0/search?q=NM\"}", "https://api.bing.microsoft.com/v7.0/search?q=NM")]
    [InlineData("{\"query\":\"NM\"}", null)]
    [InlineData("not json", null)]
    [InlineData("[]", null)]
    [InlineData(null, null)]
    public void ExtractRequestUrl_ReadsBingGroundingCallArguments(string? arguments, string? expected)
    {
        Assert.Equal(expected, BingCitationFormatter.ExtractRequestUrl(arguments));
    }
}
