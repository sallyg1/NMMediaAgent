using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NmTrendIngestion.Models;

namespace NmTrendIngestion.Sources;

/// <summary>Polls the configured local New Mexico RSS feeds (KRQE, KOB4, NM InDepth by default).</summary>
public sealed class RssSource : IArticleSource
{
    private static readonly string[] DefaultFeeds =
    {
        "https://www.krqe.com/feed/",
        "https://www.kob.com/feed/",
        "https://nmindepth.com/feed/"
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RssSource> _logger;

    public RssSource(HttpClient httpClient, IConfiguration configuration, ILogger<RssSource> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public string SourceName => "LocalRss";

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken)
    {
        var feedUrls = _configuration.GetSection("Rss:Feeds").Get<string[]>() ?? DefaultFeeds;
        var articles = new List<Article>();

        foreach (var feedUrl in feedUrls)
        {
            try
            {
                using var stream = await _httpClient.GetStreamAsync(feedUrl, cancellationToken);
                using var xmlReader = XmlReader.Create(stream);
                var feed = SyndicationFeed.Load(xmlReader);

                articles.AddRange(feed.Items.Select(item =>
                {
                    var link = item.Links.FirstOrDefault()?.Uri.ToString() ?? string.Empty;
                    var summary = item.Summary?.Text;

                    return new Article(
                        SourceName,
                        item.Title?.Text ?? string.Empty,
                        link,
                        item.PublishDate,
                        summary,
                        JsonSerializer.Serialize(new { item.Title?.Text, Link = link, item.PublishDate, Summary = summary }));
                }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll RSS feed {FeedUrl}", feedUrl);
            }
        }

        return articles;
    }
}
