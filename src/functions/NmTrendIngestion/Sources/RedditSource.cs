using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NmTrendIngestion.Models;

namespace NmTrendIngestion.Sources;

/// <summary>Polls the public Reddit JSON listing endpoint (read-only, no OAuth) for a configured subreddit.
/// Reddit API production licensing terms are an open question carried over from the original architecture deck.</summary>
public sealed class RedditSource : IArticleSource
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedditSource> _logger;

    public RedditSource(HttpClient httpClient, IConfiguration configuration, ILogger<RedditSource> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public string SourceName => "Reddit";

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken)
    {
        var subreddit = _configuration["Reddit:Subreddit"] ?? "NewMexico";

        using var response = await _httpClient.GetAsync($"/r/{subreddit}/new.json?limit=25", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RedditListing>(cancellationToken: cancellationToken);
        var children = payload?.Data?.Children ?? new List<RedditChild>();

        return children
            .Select(c => c.Data)
            .Where(d => d is not null)
            .Select(d => new Article(
                SourceName,
                d!.Title,
                $"https://www.reddit.com{d.Permalink}",
                DateTimeOffset.FromUnixTimeSeconds((long)d.CreatedUtc),
                d.Selftext,
                JsonSerializer.Serialize(d)))
            .ToList();
    }

    private sealed record RedditListing([property: JsonPropertyName("data")] RedditListingData? Data);

    private sealed record RedditListingData([property: JsonPropertyName("children")] List<RedditChild>? Children);

    private sealed record RedditChild([property: JsonPropertyName("data")] RedditPost? Data);

    private sealed record RedditPost(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("permalink")] string Permalink,
        [property: JsonPropertyName("created_utc")] double CreatedUtc,
        [property: JsonPropertyName("selftext")] string? Selftext);
}
