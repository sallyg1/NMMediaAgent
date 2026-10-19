namespace NmTrendIngestion.Agent;

/// <summary>The dual-format digest produced by a single agent run.</summary>
public sealed record DigestResult(string Markdown, string Html, int ArticleCount);
