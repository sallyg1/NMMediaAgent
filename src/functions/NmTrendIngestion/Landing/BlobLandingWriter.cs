using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using NmTrendIngestion.Models;

namespace NmTrendIngestion.Landing;

/// <summary>Writes deduped article payloads as JSON blobs to the raw-landing container, partitioned by date.</summary>
public sealed class BlobLandingWriter
{
    private readonly BlobContainerClient _containerClient;

    public BlobLandingWriter(BlobContainerClient containerClient)
    {
        _containerClient = containerClient;
    }

    public async Task WriteAsync(Article article, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = Dedup.DedupService.ComputeUrlHash(article.Url);
        var blobPath = $"{now:yyyy}/{now:MM}/{now:dd}/{article.Source}/{hash}.json";

        var blobClient = _containerClient.GetBlobClient(blobPath);
        var json = JsonSerializer.Serialize(article);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blobClient.UploadAsync(stream, overwrite: true, cancellationToken);
    }
}
