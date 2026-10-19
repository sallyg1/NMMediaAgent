#pragma warning disable OPENAI001

using System.ClientModel.Primitives;
using NmTrendIngestion.Agent;
using OpenAI;
using OpenAI.Responses;
using Xunit;

namespace NmTrendIngestion.Tests;

public class TrendAgentServiceResponseTests
{
    private const string ResponseJson = """
        {
          "id": "resp_1", "object": "response", "created_at": 1, "status": "completed", "model": "gpt-4.1-mini",
          "tools": [], "parallel_tool_calls": true,
          "output": [
            {
              "type": "bing_grounding_call", "id": "bgc_1", "call_id": "call_1", "status": "completed",
              "arguments": "{\"requesturl\":\"https://api.bing.microsoft.com/v7.0/search?q=NM%20road%20funding\"}"
            },
            {
              "type": "bing_grounding_call", "id": "bgc_2", "call_id": "call_2", "status": "completed",
              "arguments": "{\"requesturl\":\"https://api.bing.microsoft.com/v7.0/search?q=NM%20road%20funding\"}"
            },
            { "type": "bing_grounding_call_output", "id": "bgo_1", "call_id": "call_1", "status": "completed", "output": "x" },
            {
              "type": "message", "id": "msg_1", "role": "assistant", "status": "completed",
              "content": [
                {
                  "type": "output_text",
                  "text": "Roads funded【3:0†source】.",
                  "annotations": [
                    { "type": "url_citation", "url": "https://www.krqe.com/roads", "title": "KRQE", "start_index": 12, "end_index": 24 },
                    { "type": "url_citation", "url": "https://www.kob.com/roads", "title": "KOB", "start_index": 0, "end_index": 5 }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static ResponseResult ReadResponse() =>
        ModelReaderWriter.Read<ResponseResult>(BinaryData.FromString(ResponseJson), ModelReaderWriterOptions.Json, OpenAIContext.Default)!;

    [Fact]
    public void ExtractAssistantMessage_CapturesMarkerSpansAndUnanchoredCitations()
    {
        var (text, citations) = TrendAgentService.ExtractAssistantMessage(ReadResponse());

        Assert.Equal("Roads funded【3:0†source】.", text);
        Assert.Collection(
            citations,
            c => Assert.Equal(new UrlCitation("【3:0†source】", "https://www.krqe.com/roads", "KRQE"), c),
            c => Assert.Equal(new UrlCitation(string.Empty, "https://www.kob.com/roads", "KOB"), c));
    }

    [Fact]
    public void GetBingSearchQueries_ReadsDistinctQueriesFromBingGroundingCalls()
    {
        var queries = TrendAgentService.GetBingSearchQueries(ReadResponse());

        var query = Assert.Single(queries);
        Assert.Equal("NM road funding", query.Query);
        Assert.Equal("https://www.bing.com/search?q=NM%20road%20funding", query.Url);
    }
}
