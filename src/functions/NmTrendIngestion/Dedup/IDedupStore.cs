namespace NmTrendIngestion.Dedup;

/// <summary>Storage-agnostic seen-URL store, kept separate from DedupService so the Azure SDK client type
/// never has to be mocked directly in tests.</summary>
public interface IDedupStore
{
    /// <returns>true if the entry was newly inserted; false if it already existed (duplicate).</returns>
    Task<bool> TryInsertAsync(string partitionKey, string rowKey, string url, CancellationToken cancellationToken);
}
