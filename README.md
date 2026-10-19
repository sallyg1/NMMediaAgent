# media-agent: NM Trend Intelligence Agent

Daily trend digest for New Mexico state executive staff. The pipeline collects New Mexico news, uses an Azure AI Foundry agent to identify the day's top five trends, and emails an executive digest.

## Architecture

[![NMMediaAgent architecture: public RSS inputs, a daily Logic Apps Consumption workflow, Azure Functions, Storage, a versioned Foundry agent, and an Outlook email digest.](docs/output/architecture/nm-media-agent-architecture.svg?v=015ec73)](docs/output/architecture/nm-media-agent-architecture.svg?v=015ec73)

[PNG image](docs/output/architecture/nm-media-agent-architecture.png?v=015ec73) | [Editable Excalidraw source](docs/output/architecture/nm-media-agent-architecture.excalidraw)

The diagram describes the checked-in infrastructure and runtime, not live deployment status. Both HTTP functions run in the same .NET 10 isolated / Flex Consumption Function App. Bing grounding and storage private networking are configurable; the model defaults to `gpt-4.1-mini`. The email path shown is the Office 365 Outlook connector in `infra/modules/logicapp.bicep` and requires an authorized work/school sender mailbox.

## How it works

```text
Logic App (Consumption), daily at 06:00 Mountain
  1. POST /api/ingestfeeds       -> Function App
       RSS (KRQE, KOB, NM InDepth)
       -> dedup by URL hash (Table: urlDedupCache)
       -> store new articles (Blob: raw-landing/yyyy/MM/dd/...)
  2. POST /api/synthesizetrends  -> Function App
       -> read today's articles from Blob
       -> Foundry agent (GPT-4.1-mini + Grounding with Bing)
       -> Markdown + HTML digest
  3. Office 365 Outlook          -> email the HTML digest
```

## Target environment

| Layer | Cloud | Notes |
|---|---|---|
| Azure resources (Foundry, Bing Grounding, Function App, Storage, Logic App) | Azure Commercial | Grounding with Bing isn't available in Azure Government. |
| Digest mailbox (Exchange Online) | Microsoft 365 GCC (Moderate) | GCC uses commercial Entra ID and the [worldwide Microsoft 365 endpoints](https://learn.microsoft.com/microsoft-365/enterprise/microsoft-365-endpoints), so the Logic App's Office 365 Outlook connector can sign in to GCC mailboxes. This doesn't apply to GCC High or DoD. |

The digest is generated in Azure Commercial and emailed into M365 GCC. Its inputs are public news only.

## Repository layout

| Path | Contents |
|---|---|
| `infra/` | Bicep: optional VNet and private endpoints, storage, Function App, Logic App, Foundry (plus a connection to the manually created Bing resource), monitoring, RBAC |
| `src/functions/NmTrendIngestion/` | .NET 10 isolated Azure Functions app |
| `src/agent/system_prompt.md` | Foundry agent instructions |
| `tests/NmTrendIngestion.Tests/` | xUnit tests |
| `logicapp/` | Earlier Logic App Standard workflow project; not used by the current Consumption deployment |
| `.github/plan-nmTrendAgentBuild.prompt.md` | Original build plan |
| `PROMPT-New-Mexico-Agent-Intelligence-Dashboard.md` | Prompt used to generate the digest dashboard |

## Design notes

- **Logic App Consumption, not Standard.** Standard on the Workflow Standard (WS1) plan needs storage-account key access for its Azure Files content share. The target subscription's policy disables shared-key access.
- **Grounding with Bing.** Data sent to Bing leaves the Azure compliance and geo boundary, isn't covered by the Microsoft Data Protection Addendum, and waives elevated GCC compliance commitments ([web grounding overview](https://learn.microsoft.com/azure/foundry/agents/how-to/tools/web-overview)). The customer's security and compliance team must approve its use. It isn't available in Azure Government, and it shouldn't be described as FedRAMP High or CJIS compliant. Bing citations and search queries are rendered as links, per Bing's use and display requirements.
- **Foundry Agent Service (current, not classic).** The app uses `Azure.AI.Projects` 2.x. On first use it publishes a versioned prompt agent (`nm-trend-intelligence-agent`) and reuses the latest version while the model, system prompt, and Bing connection are unchanged; any change creates a new version. Each digest is a single Responses API call pinned to that agent version, with no threads, runs, or polling. The Function App identity needs the **Foundry User** role on the Foundry account.

## Build and test

```bash
dotnet test tests/NmTrendIngestion.Tests/NmTrendIngestion.Tests.csproj
```

## Manual prerequisite: create the Grounding with Bing Search resource

The customer creates this resource in the Azure portal, not through Bicep, so their admin explicitly accepts the [Grounding with Bing terms of use](https://www.microsoft.com/en-us/bing/apis/grounding-legal-enterprise). Microsoft treats creating the resource through a template or the CLI as acceptance too, so creating it in the portal keeps the acceptance a deliberate, recorded customer action. Everything else, including the Foundry connection to this resource, is deployed by Bicep.

Before you start:

- Get written approval from the customer's legal and security teams. Grounding with Bing sends data outside the Azure compliance boundary, isn't covered by the Microsoft Data Protection Addendum, and waives elevated GCC compliance commitments. Use requires Bing attribution, unmodified results, and no use of results to train AI models.
- Roles: **Contributor** on the subscription to register the resource provider (once per subscription), and **Contributor** on the resource group to create the resource.

Steps:

1. Create the resource group, if it doesn't exist: `az group create -n <resource-group> -l <region>`.
2. If this is a new subscription, register the provider: Azure portal > **Subscriptions** > your subscription > **Resource providers** > `Microsoft.Bing` > **Register**.
3. Azure portal > **Create a resource** > search **Grounding with Bing Search** > **Create**.
4. Select the subscription and resource group, enter the name (default `bing-nmtrenddev`, matching `bingResourceName` in `infra/main.parameters.json`), and select pricing tier **G1**. The resource region is Global.
5. Review the terms on the page, select the acknowledgment checkbox, then **Review + create** > **Create**.
6. Record who accepted the terms and when, for the customer's records.

If the Bing resource is in a different resource group than the rest of the deployment, also set `bingResourceGroupName` in `infra/main.parameters.json`.

## Deploy infrastructure

Set `digestRecipient`, `enablePrivateNetworking`, and `bingResourceName` in `infra/main.parameters.json`, then:

```bash
az deployment group what-if -g <resource-group> -f infra/main.bicep -p infra/main.parameters.json
az deployment group create  -g <resource-group> -f infra/main.bicep -p infra/main.parameters.json
```

The deployment fails if the Bing resource doesn't exist yet. The deploying identity needs **Owner** (or **Contributor** + **User Access Administrator**) on the resource group, because the templates create role assignments and read the Bing resource key for the Foundry connection.

After the first deployment, authorize the `office365-<prefix>` API connection in the Azure portal before the first email can be sent. Sign in with the sender account from the customer's M365 GCC tenant, preferably a shared or service mailbox rather than a personal one. If the GCC tenant's Conditional Access policies block the sign-in, the customer's identity team must allow it.
