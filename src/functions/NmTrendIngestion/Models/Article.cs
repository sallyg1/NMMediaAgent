namespace NmTrendIngestion.Models;

/// <summary>A single deduped article/post payload landed to blob storage as the immutable audit record.</summary>
public sealed record Article(
    string Source,
    string Title,
    string Url,
    DateTimeOffset PublishedAt,
    string? Summary,
    string RawJson);
