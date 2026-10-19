using System.Security.Cryptography;
using System.Text;

namespace NmTrendIngestion.Dedup;

/// <summary>URL-hash dedup cache (PartitionKey=source, RowKey=SHA-256 hash of URL) backed by an IDedupStore.</summary>
public sealed class DedupService
{
    private readonly IDedupStore _store;

    public DedupService(IDedupStore store)
    {
        _store = store;
    }

    /// <returns>true when the URL is newly recorded; false when it was already a known duplicate.</returns>
    public Task<bool> TryMarkAsSeenAsync(string source, string url, CancellationToken cancellationToken)
    {
        var hash = ComputeUrlHash(url);
        return _store.TryInsertAsync(source, hash, url, cancellationToken);
    }

    public static string ComputeUrlHash(string url)
    {
        var bytes = Encoding.UTF8.GetBytes(url);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
