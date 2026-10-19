using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NmTrendIngestion.Dedup;
using NmTrendIngestion.Landing;
using NmTrendIngestion.Models;
using NmTrendIngestion.Sources;

namespace NmTrendIngestion;

/// <summary>HTTP-triggered so the Phase 4 Logic App can invoke it via a managed-identity-authenticated call.</summary>
public sealed class IngestionFunction
{
    private readonly IEnumerable<IArticleSource> _sources;
    private readonly DedupService _dedupService;
    private readonly BlobLandingWriter _landingWriter;
    private readonly ILogger<IngestionFunction> _logger;

    public IngestionFunction(
        IEnumerable<IArticleSource> sources,
        DedupService dedupService,
        BlobLandingWriter landingWriter,
        ILogger<IngestionFunction> logger)
    {
        _sources = sources;
        _dedupService = dedupService;
        _landingWriter = landingWriter;
        _logger = logger;
    }

    [Function("IngestFeeds")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var totalFetched = 0;
        var totalLanded = 0;

        foreach (var source in _sources)
        {
            IReadOnlyList<Article> articles;
            try
            {
                articles = await source.FetchAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Source {Source} failed to fetch articles.", source.SourceName);
                continue;
            }

            totalFetched += articles.Count;

            foreach (var article in articles)
            {
                var isNew = await _dedupService.TryMarkAsSeenAsync(article.Source, article.Url, cancellationToken);
                if (!isNew)
                {
                    continue;
                }

                await _landingWriter.WriteAsync(article, cancellationToken);
                totalLanded++;
            }
        }

        _logger.LogInformation("Ingestion run complete. Fetched {Fetched}, landed {Landed}.", totalFetched, totalLanded);

        return new OkObjectResult(new { fetched = totalFetched, landed = totalLanded });
    }
}
