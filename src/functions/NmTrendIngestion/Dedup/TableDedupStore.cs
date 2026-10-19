using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace NmTrendIngestion.Dedup;

/// <summary>Table Storage-backed IDedupStore. Insert-if-not-exists via AddEntity, treating HTTP 409 as "duplicate".</summary>
public sealed class TableDedupStore : IDedupStore
{
    private readonly TableClient _tableClient;
    private readonly ILogger<TableDedupStore> _logger;

    public TableDedupStore(TableClient tableClient, ILogger<TableDedupStore> logger)
    {
        _tableClient = tableClient;
        _logger = logger;
    }

    public async Task<bool> TryInsertAsync(string partitionKey, string rowKey, string url, CancellationToken cancellationToken)
    {
        var entity = new TableEntity(partitionKey, rowKey)
        {
            { "Url", url },
            { "SeenAt", DateTimeOffset.UtcNow }
        };

        try
        {
            await _tableClient.AddEntityAsync(entity, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            _logger.LogDebug("Duplicate entry for partition {PartitionKey}, row {RowKey}.", partitionKey, rowKey);
            return false;
        }
    }
}
