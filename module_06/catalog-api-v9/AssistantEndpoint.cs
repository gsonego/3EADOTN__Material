using System.Text;
using System.Text.Json;
using catalog_api.Models;
using Microsoft.Azure.Cosmos;

namespace catalog_api;

// The whole AI feature, in one file, on purpose: this is what gets shown on
// screen in class. Deliberately no OpenAI SDK -- a plain HttpClient and raw
// JSON, so students see the actual shape of a chat-completions request (a list
// of messages, each with a role) instead of an SDK abstraction hiding it.
// It also avoids repeating Module 4's transitive-package gotcha, where adding
// an Azure SDK bumped Azure.Core and broke DefaultAzureCredential locally.
public static class AssistantEndpoint
{
    // Deliberately generic. This is the "before" state the class starts from:
    // helpful, confident, and completely unaware of what this shop actually sells.
    private const string DefaultSystemPrompt =
        "You are the assistant for our film catalog app. Answer the customer's question.";

    public static void MapAssistant(this WebApplication app, Container titlesContainer)
    {
        app.MapPost("/assistant", async (
            AssistantRequest request,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<Program> logger) =>
        {
            var endpoint   = config["AzureOpenAI:Endpoint"];
            var deployment = config["AzureOpenAI:Deployment"];
            var apiKey     = config["AzureOpenAI:ApiKey"];
            // Pinned because reasoning_effort below does not exist on older
            // API versions -- see the Module 6 manual's Issues & Fixes.
            var apiVersion = config["AzureOpenAI:ApiVersion"] ?? "2025-04-01-preview";

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(apiKey))
            {
                return Results.Problem(
                    "Azure OpenAI is not configured. Set AzureOpenAI:Endpoint, :Deployment and :ApiKey.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            // ---- Build the messages array. This IS the lesson. ----
            var messages = new List<object>
            {
                // 1. The system message: the app's own instructions to the model.
                //    The user never sees or controls this -- which is exactly why
                //    it is where behaviour rules belong.
                new { role = "system",
                      content = string.IsNullOrWhiteSpace(request.SystemPrompt) ? DefaultSystemPrompt : request.SystemPrompt }
            };

            // 2. Trusted context: the application hands the model the facts it
            //    is allowed to use. No RAG, no embeddings, no vector database --
            //    just the rows we already have, serialised into the prompt.
            var contextTitles = 0;
            if (request.Grounded)
            {
                var catalog = new StringBuilder();
                catalog.AppendLine("CATALOG (the complete list of titles this shop has):");
                using var iterator = titlesContainer.GetItemQueryIterator<TitleItem>("SELECT * FROM c");
                while (iterator.HasMoreResults)
                {
                    foreach (var item in await iterator.ReadNextAsync())
                    {
                        catalog.AppendLine($"{item.Title} | {item.Genre} | {item.Year} | {item.Description}");
                        contextTitles++;
                    }
                }
                messages.Add(new { role = "system", content = catalog.ToString() });
            }

            // 3. Conversation history. The model remembers nothing between HTTP
            //    requests -- the application resends what it wants remembered.
            foreach (var turn in request.History ?? new List<ChatTurn>())
            {
                messages.Add(new { role = turn.Role, content = turn.Content });
            }

            // 4. Finally, what the user just typed.
            messages.Add(new { role = "user", content = request.Question });

            // reasoning_effort "minimal" matters a lot: gpt-5-mini is a reasoning
            // model, and without this a simple answer costs ~4.2s and ~256 hidden
            // reasoning tokens. With it: ~1.2s and none. Measured, not assumed.
            var payload = new { messages, reasoning_effort = "minimal" };

            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            var uri = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri);
            httpRequest.Headers.Add("api-key", apiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(httpRequest);
            var raw = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // 429 (rate limit) and 400 (bad request) both land here. The body
                // is logged rather than returned -- it can echo the prompt back.
                logger.LogError("Azure OpenAI returned {Status}: {Body}", (int)response.StatusCode, raw);
                return Results.Problem(
                    $"The model endpoint returned {(int)response.StatusCode}.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            using var doc = JsonDocument.Parse(raw);
            var answer = doc.RootElement.GetProperty("choices")[0]
                                        .GetProperty("message")
                                        .GetProperty("content").GetString() ?? string.Empty;
            var usage = doc.RootElement.GetProperty("usage");
            var model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : deployment;

            logger.LogInformation(
                "Assistant answered. Grounded={Grounded}, ContextTitles={ContextTitles}, PromptTokens={PromptTokens}.",
                request.Grounded, contextTitles, usage.GetProperty("prompt_tokens").GetInt32());

            return Results.Ok(new AssistantResponse(
                answer,
                model,
                request.Grounded,
                contextTitles,
                new AssistantUsage(
                    usage.GetProperty("prompt_tokens").GetInt32(),
                    usage.GetProperty("completion_tokens").GetInt32())));
        })
        .WithName("AskAssistant")
        .WithOpenApi();
    }
}
