using Azure.AI.Projects;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NmTrendIngestion.Agent;
using NmTrendIngestion.Dedup;
using NmTrendIngestion.Landing;
using NmTrendIngestion.Sources;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

var storageAccountName = builder.Configuration["Storage:AccountName"]
    ?? throw new InvalidOperationException("Storage:AccountName app setting is required.");

// Identity-based access only — no storage account shared keys per the private-networking decision.
builder.Services.AddSingleton(new TableClient(
    new Uri($"https://{storageAccountName}.table.core.windows.net"),
    "urlDedupCache",
    new DefaultAzureCredential()));

builder.Services.AddSingleton(new BlobContainerClient(
    new Uri($"https://{storageAccountName}.blob.core.windows.net/raw-landing"),
    new DefaultAzureCredential()));

builder.Services.AddHttpClient<RedditSource>(client =>
{
    client.BaseAddress = new Uri("https://www.reddit.com");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("NmTrendIntelligenceAgent/1.0 (state-of-nm-trend-digest)");
});

builder.Services.AddHttpClient<RssSource>();

builder.Services.AddSingleton<IArticleSource>(sp => sp.GetRequiredService<RedditSource>());
builder.Services.AddSingleton<IArticleSource>(sp => sp.GetRequiredService<RssSource>());

builder.Services.AddSingleton<IDedupStore, TableDedupStore>();
builder.Services.AddSingleton<DedupService>();
builder.Services.AddSingleton<BlobLandingWriter>();

var foundryProjectEndpoint = builder.Configuration["Foundry:ProjectEndpoint"]
    ?? throw new InvalidOperationException("Foundry:ProjectEndpoint app setting is required.");
var modelDeploymentName = builder.Configuration["Foundry:ModelDeploymentName"]
    ?? throw new InvalidOperationException("Foundry:ModelDeploymentName app setting is required.");
var bingConnectionId = builder.Configuration["Foundry:BingConnectionId"];
var systemPromptPath = Path.Combine(AppContext.BaseDirectory, "system_prompt.md");
var systemPrompt = File.ReadAllText(systemPromptPath);

builder.Services.AddSingleton(new AIProjectClient(new Uri(foundryProjectEndpoint), new DefaultAzureCredential()));
builder.Services.AddSingleton(sp => new TrendAgentService(
    sp.GetRequiredService<AIProjectClient>(),
    modelDeploymentName,
    bingConnectionId,
    systemPrompt));

builder.Build().Run();
