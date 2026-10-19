using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace NmTrendIngestion.Agent;

/// <summary>
/// A website citation returned by the agent: the in-text marker and the URL it stands for.
/// An empty marker means the citation has no inline position and is listed as a source instead.
/// </summary>
public sealed record UrlCitation(string Marker, string Url, string? Title);

/// <summary>A Bing query the agent ran, as a user-facing bing.com link.</summary>
public sealed record BingSearchQuery(string Query, string Url);

/// <summary>
/// Renders Grounding with Bing references per its use and display requirements: website citations replace the
/// agent's in-text markers, and the Bing search queries are shown as bing.com links.
/// </summary>
public static partial class BingCitationFormatter
{
    [GeneratedRegex("【[^】]*】")]
    private static partial Regex CitationMarkerRegex();

    [GeneratedRegex("^【[^】]*】$")]
    private static partial Regex WholeCitationMarkerRegex();

    public static string ResolveMarkdown(string markdown, IEnumerable<UrlCitation> citations) =>
        Resolve(
            markdown,
            citations,
            c => $"[{EscapeMarkdown(LinkText(c))}]({c.Url})",
            links => $"\n\n**Sources:** {string.Join(" · ", links)}");

    public static string ResolveHtml(string html, IEnumerable<UrlCitation> citations) =>
        Resolve(
            html,
            citations,
            c => $"<a href=\"{WebUtility.HtmlEncode(c.Url)}\">{WebUtility.HtmlEncode(LinkText(c))}</a>",
            links => $"\n<p><strong>Sources:</strong> {string.Join(" · ", links)}</p>");

    public static bool IsCitationMarker(string? text) =>
        !string.IsNullOrEmpty(text) && WholeCitationMarkerRegex().IsMatch(text);

    /// <summary>Reads the Bing API request URL from a <c>bing_grounding_call</c> item's JSON arguments.</summary>
    public static string? ExtractRequestUrl(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if ((property.NameEquals("requesturl") || property.NameEquals("request_url"))
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static BingSearchQuery? ToBingSearchQuery(string requestUrl)
    {
        if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = HttpUtility.ParseQueryString(uri.Query)["q"];
        return string.IsNullOrWhiteSpace(query)
            ? null
            : new BingSearchQuery(query, $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}");
    }

    public static string AppendSearchQueriesMarkdown(string markdown, IReadOnlyList<BingSearchQuery> queries)
    {
        if (queries.Count == 0)
        {
            return markdown;
        }

        var links = string.Join(" · ", queries.Select(q => $"[{EscapeMarkdown(q.Query)}]({q.Url})"));
        return $"{markdown}\n\n**Searched on Bing:** {links}";
    }

    public static string AppendSearchQueriesHtml(string html, IReadOnlyList<BingSearchQuery> queries)
    {
        if (queries.Count == 0)
        {
            return html;
        }

        var links = string.Join(" · ", queries.Select(q =>
            $"<a href=\"{WebUtility.HtmlEncode(q.Url)}\">{WebUtility.HtmlEncode(q.Query)}</a>"));
        return $"{html}\n<p><strong>Searched on Bing:</strong> {links}</p>";
    }

    private static string Resolve(
        string text,
        IEnumerable<UrlCitation> citations,
        Func<UrlCitation, string> formatLink,
        Func<IEnumerable<string>, string> formatSources)
    {
        var webCitations = citations.Where(c => IsWebUrl(c.Url)).ToList();

        foreach (var citation in webCitations.Where(c => !string.IsNullOrEmpty(c.Marker)))
        {
            text = text.Replace(citation.Marker, formatLink(citation), StringComparison.Ordinal);
        }

        // Markers without a usable citation would otherwise render as raw 【n:m†source】 text.
        text = CitationMarkerRegex().Replace(text, string.Empty);

        // Bing's display requirements still apply to citations the model didn't anchor inline.
        var unplaced = webCitations
            .Where(c => string.IsNullOrEmpty(c.Marker))
            .DistinctBy(c => c.Url)
            .Select(formatLink)
            .ToList();

        return unplaced.Count == 0 ? text : text + formatSources(unplaced);
    }

    private static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string LinkText(UrlCitation citation) =>
        string.IsNullOrWhiteSpace(citation.Title) ? new Uri(citation.Url).Host : citation.Title;

    private static string EscapeMarkdown(string text) =>
        text.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
}
