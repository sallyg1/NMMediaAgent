using NmTrendIngestion.Models;

namespace NmTrendIngestion.Sources;

public interface IArticleSource
{
    string SourceName { get; }

    Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken);
}
