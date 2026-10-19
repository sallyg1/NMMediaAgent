// The OpenAI .NET SDK marks the Responses API types as evaluation-only (OPENAI001); the Foundry Agent Service runs on them.
#pragma warning disable OPENAI001

using System.ClientModel;
using System.Net;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using OpenAI.Responses;

namespace NmTrendIngestion.Agent;

/// <summary>
/// Gets-or-creates a versioned Foundry prompt agent, sends the day's articles through the Responses API, and returns the digest.
/// </summary>
public sealed class TrendAgentService
{
    private const string AgentName = "nm-trend-intelligence-agent";

    // The Logic App's HTTP call to the Function is capped at 230 seconds, so a single response must finish well inside that.
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(3);

    private readonly AIProjectClient _client;
    private readonly ProjectOpenAIClient _openAIClient;
    private readonly string _modelDeploymentName;
    private readonly string? _bingConnectionId;
    private readonly string _systemPrompt;
    private readonly SemaphoreSlim _agentLock = new(1, 1);
    private ProjectsAgentVersion? _agentVersion;

    public TrendAgentService(
        AIProjectClient client,
        string modelDeploymentName,
        string? bingConnectionId,
        string systemPrompt)
    {
        _client = client;
        _openAIClient = client.GetProjectOpenAIClient(new ProjectOpenAIClientOptions { NetworkTimeout = ResponseTimeout });
        _modelDeploymentName = modelDeploymentName;
        _bingConnectionId = bingConnectionId;
        _systemPrompt = systemPrompt;
    }

    public async Task<DigestResult> SynthesizeAsync(string articlesJson, int articleCount, CancellationToken cancellationToken)
    {
        var agentVersion = await GetOrCreateAgentVersionAsync(cancellationToken);

        ProjectResponsesClient responsesClient = _openAIClient.GetProjectResponsesClientForAgent(
            new AgentReference(agentVersion.Name, agentVersion.Version),
            defaultConversationId: null);

        ResponseResult response = (await responsesClient.CreateResponseAsync(
            $"Today's deduped NM articles (JSON array of {{source,title,url,publishedAt,summary}}):\n{articlesJson}",
            previousResponseId: null,
            cancellationToken)).Value;

        if (response.Status != ResponseStatus.Completed)
        {
            var reason = response.Error?.Message ?? response.IncompleteStatusDetails?.Reason?.ToString();
            throw new InvalidOperationException($"Agent response {response.Id} finished with status {response.Status}: {reason}");
        }

        var (responseText, citations) = ExtractAssistantMessage(response);
        var searchQueries = GetBingSearchQueries(response);
        var (markdown, html) = SplitSections(responseText);

        markdown = BingCitationFormatter.AppendSearchQueriesMarkdown(
            BingCitationFormatter.ResolveMarkdown(markdown, citations), searchQueries);
        html = BingCitationFormatter.AppendSearchQueriesHtml(
            BingCitationFormatter.ResolveHtml(html, citations), searchQueries);

        return new DigestResult(markdown, html, articleCount);
    }

    internal static IReadOnlyList<BingSearchQuery> GetBingSearchQueries(ResponseResult response)
    {
        var queries = new List<BingSearchQuery>();

        foreach (var item in response.OutputItems)
        {
            if (item.AsAgentResponseItem() is BingGroundingToolCall bingCall
                && BingCitationFormatter.ExtractRequestUrl(bingCall.Arguments) is { } requestUrl
                && BingCitationFormatter.ToBingSearchQuery(requestUrl) is { } query
                && !queries.Any(q => q.Url == query.Url))
            {
                queries.Add(query);
            }
        }

        return queries;
    }

    /// <summary>
    /// Reuses the latest agent version when it already matches the configured model, prompt, and Bing connection;
    /// otherwise publishes a new version so prompt or model changes take effect without manual cleanup.
    /// </summary>
    private async Task<ProjectsAgentVersion> GetOrCreateAgentVersionAsync(CancellationToken cancellationToken)
    {
        if (_agentVersion is not null)
        {
            return _agentVersion;
        }

        await _agentLock.WaitAsync(cancellationToken);
        try
        {
            if (_agentVersion is not null)
            {
                return _agentVersion;
            }

            var latest = await TryGetLatestAgentVersionAsync(cancellationToken);
            if (latest is not null && MatchesConfiguration(latest))
            {
                return _agentVersion = latest;
            }

            var definition = new DeclarativeAgentDefinition(_modelDeploymentName)
            {
                Instructions = _systemPrompt
            };

            if (!string.IsNullOrWhiteSpace(_bingConnectionId))
            {
                definition.Tools.Add(new BingGroundingTool(new BingGroundingSearchToolOptions(
                    new[] { new BingGroundingSearchConfiguration(_bingConnectionId) })));
            }

            return _agentVersion = (await _client.AgentAdministrationClient.CreateAgentVersionAsync(
                AgentName,
                new ProjectsAgentVersionCreationOptions(definition),
                cancellationToken: cancellationToken)).Value;
        }
        finally
        {
            _agentLock.Release();
        }
    }

    private async Task<ProjectsAgentVersion?> TryGetLatestAgentVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProjectsAgentRecord agent = (await _client.AgentAdministrationClient.GetAgentAsync(AgentName, cancellationToken)).Value;
            return agent.GetLatestVersion();
        }
        catch (ClientResultException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private bool MatchesConfiguration(ProjectsAgentVersion version)
    {
        if (version.Definition is not DeclarativeAgentDefinition definition
            || definition.Model != _modelDeploymentName
            || definition.Instructions != _systemPrompt)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_bingConnectionId))
        {
            return definition.Tools.Count == 0;
        }

        return definition.Tools.Count == 1
            && definition.Tools[0].AsAgentTool() is BingGroundingTool bingTool
            && bingTool.SearchToolOptions.SearchConfigurations.Any(c => c.ProjectConnectionId == _bingConnectionId);
    }

    internal static (string Text, IReadOnlyList<UrlCitation> Citations) ExtractAssistantMessage(ResponseResult response)
    {
        var message = response.OutputItems
            .OfType<MessageResponseItem>()
            .LastOrDefault(m => m.Role == MessageRole.Assistant)
            ?? throw new InvalidOperationException($"Agent response {response.Id} completed but produced no assistant message.");

        var text = new System.Text.StringBuilder();
        var citations = new List<UrlCitation>();

        foreach (var part in message.Content)
        {
            if (string.IsNullOrEmpty(part.Text))
            {
                continue;
            }

            foreach (var annotation in part.OutputTextAnnotations.OfType<UriCitationMessageAnnotation>())
            {
                if (annotation.Uri is null)
                {
                    continue;
                }

                // Annotations are offsets into this part's text; capture the cited span so it can be resolved after the
                // response is split into its Markdown and HTML sections.
                var span = annotation.StartIndex >= 0 && annotation.EndIndex > annotation.StartIndex && annotation.EndIndex <= part.Text.Length
                    ? part.Text[annotation.StartIndex..annotation.EndIndex]
                    : string.Empty;
                var marker = BingCitationFormatter.IsCitationMarker(span) ? span : string.Empty;

                citations.Add(new UrlCitation(marker, annotation.Uri.ToString(), annotation.Title));
            }

            text.Append(part.Text);
        }

        if (text.Length == 0)
        {
            throw new InvalidOperationException($"Agent response {response.Id} completed but produced no assistant text.");
        }

        return (text.ToString(), citations);
    }

    private static (string Markdown, string Html) SplitSections(string responseText)
    {
        const string markdownMarker = "### MARKDOWN";
        const string htmlMarker = "### HTML";

        var markdownIndex = responseText.IndexOf(markdownMarker, StringComparison.OrdinalIgnoreCase);
        var htmlIndex = responseText.IndexOf(htmlMarker, StringComparison.OrdinalIgnoreCase);

        if (markdownIndex < 0 || htmlIndex < 0 || htmlIndex <= markdownIndex)
        {
            var fallbackMarkdown = markdownIndex >= 0
                ? responseText[(markdownIndex + markdownMarker.Length)..].Trim()
                : responseText.Trim();
            return (fallbackMarkdown, BuildFallbackHtml(fallbackMarkdown));
        }

        var markdown = responseText[(markdownIndex + markdownMarker.Length)..htmlIndex].Trim();
        var html = responseText[(htmlIndex + htmlMarker.Length)..].Trim();
        return (markdown, string.IsNullOrWhiteSpace(html) ? BuildFallbackHtml(markdown) : html);
    }

    private static string BuildFallbackHtml(string markdown)
    {
        var encodedMarkdown = WebUtility.HtmlEncode(markdown);
        return $"<div style=\"font-family: Segoe UI, sans-serif; white-space: pre-wrap;\">{encodedMarkdown}</div>";
    }
}
