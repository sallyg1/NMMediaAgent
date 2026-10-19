using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NmTrendIngestion.Models;

namespace NmTrendIngestion.Agent;

/// <summary>HTTP-triggered so the daily Logic App workflow can invoke it after ingestion lands the day's articles.</summary>
public sealed class AgentSynthesisFunction
{
    private readonly BlobContainerClient _containerClient;
    private readonly TrendAgentService _agentService;
    private readonly ILogger<AgentSynthesisFunction> _logger;

    public AgentSynthesisFunction(
        BlobContainerClient containerClient,
        TrendAgentService agentService,
        ILogger<AgentSynthesisFunction> logger)
    {
        _containerClient = containerClient;
        _agentService = agentService;
        _logger = logger;
    }

    [Function("SynthesizeTrends")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var today = DateTimeOffset.UtcNow;
        var prefix = $"{today:yyyy}/{today:MM}/{today:dd}/";

        var articles = new List<Article>();
        await foreach (var blobItem in _containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            var blobClient = _containerClient.GetBlobClient(blobItem.Name);
            var download = await blobClient.DownloadContentAsync(cancellationToken);
            var article = download.Value.Content.ToObjectFromJson<Article>();
            if (article is not null)
            {
                articles.Add(article);
            }
        }

        if (articles.Count == 0)
        {
            _logger.LogWarning("No landed articles found for prefix {Prefix}; skipping agent synthesis.", prefix);
            return new OkObjectResult(new { markdown = string.Empty, html = string.Empty, articleCount = 0 });
        }

        var condensed = articles.Select(a => new
        {
            source = a.Source,
            title = a.Title,
            url = a.Url,
            publishedAt = a.PublishedAt,
            summary = a.Summary
        });
        var articlesJson = JsonSerializer.Serialize(condensed);

        var digest = await _agentService.SynthesizeAsync(articlesJson, articles.Count, cancellationToken);

        _logger.LogInformation("Synthesized digest from {Count} articles.", digest.ArticleCount);

        return new OkObjectResult(digest);
    }
}
