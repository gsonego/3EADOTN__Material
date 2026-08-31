using Microsoft.AspNetCore.Mvc;

namespace catalog_ui.Controllers;

// The browser only ever calls this same-origin controller. It reads the
// student's chosen Catalog API base URL + subscription key from custom
// request headers (sent by wwwroot/js/catalog.js, sourced from localStorage
// on the client) and makes the actual outbound call itself -- server-to-server
// calls aren't subject to CORS, so the browser never talks to the dynamic
// API URL directly.
[ApiController]
[Route("api")]
public class CatalogProxyController : ControllerBase
{
    private const string BaseUrlHeader = "X-Catalog-Base-Url";
    private const string ApiKeyHeader = "X-Catalog-Api-Key";
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";

    private readonly IHttpClientFactory _httpClientFactory;

    public CatalogProxyController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("health")]
    public Task<IActionResult> Health() => Forward(HttpMethod.Get, "/");

    // Module 6. Exactly the same pattern as every other route here: the browser
    // posts to this same-origin endpoint, and THIS SERVER calls the Catalog
    // API. Nothing new was needed for the AI feature -- no CORS, no new auth.
    [HttpPost("assistant")]
    public Task<IActionResult> Assistant() => Forward(HttpMethod.Post, "/assistant", forwardBody: true);

    [HttpGet("titles")]
    public Task<IActionResult> ListTitles() => Forward(HttpMethod.Get, "/titles");

    [HttpGet("titles/count")]
    public Task<IActionResult> CountTitles() => Forward(HttpMethod.Get, "/titles/count");

    [HttpPost("titles")]
    public Task<IActionResult> CreateTitle() => Forward(HttpMethod.Post, "/titles", forwardBody: true);

    [HttpPut("titles/{id}")]
    public Task<IActionResult> UpdateTitle(string id) => Forward(HttpMethod.Put, $"/titles/{id}", forwardBody: true);

    [HttpDelete("titles/{id}")]
    public Task<IActionResult> DeleteTitle(string id) => Forward(HttpMethod.Delete, $"/titles/{id}");

    [HttpPost("titles/{id}/poster")]
    public async Task<IActionResult> UploadPoster(string id, IFormFile file)
    {
        var requestMessage = BuildRequest(HttpMethod.Post, $"/titles/{id}/poster", out var error);
        if (requestMessage is null) return error!;

        // Rebuilt as a fresh MultipartFormDataContent rather than piping
        // Request.Body straight through as a raw StreamContent (the way
        // Forward() does for JSON bodies) -- that loses the original
        // Content-Length and can corrupt the multipart boundary in transit,
        // which the downstream API's Kestrel rejects with an empty-body 400
        // before the request even reaches its route handler.
        using var content = new MultipartFormDataContent();
        await using var fileStream = file.OpenReadStream();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        content.Add(streamContent, "file", file.FileName);
        requestMessage.Content = content;

        return await SendAndRelay(requestMessage);
    }

    private HttpRequestMessage? BuildRequest(HttpMethod method, string path, out IActionResult? error)
    {
        var baseUrl = Request.Headers[BaseUrlHeader].ToString();
        var apiKey = Request.Headers[ApiKeyHeader].ToString();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            error = BadRequest(new { error = "No Catalog API endpoint configured. Open Settings and set a base URL." });
            return null;
        }

        var requestMessage = new HttpRequestMessage(method, baseUrl.TrimEnd('/') + path);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            requestMessage.Headers.Add(SubscriptionKeyHeader, apiKey);
        }

        error = null;
        return requestMessage;
    }

    private async Task<IActionResult> Forward(HttpMethod method, string path, bool forwardBody = false)
    {
        var requestMessage = BuildRequest(method, path, out var error);
        if (requestMessage is null) return error!;

        if (forwardBody)
        {
            var contentType = Request.ContentType ?? "application/json";
            requestMessage.Content = new StreamContent(Request.Body);
            requestMessage.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return await SendAndRelay(requestMessage);
    }

    private async Task<IActionResult> SendAndRelay(HttpRequestMessage requestMessage)
    {
        var client = _httpClientFactory.CreateClient("catalog-api");
        try
        {
            var response = await client.SendAsync(requestMessage);
            var responseBody = await response.Content.ReadAsStringAsync();
            var responseContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

            return new ContentResult
            {
                Content = responseBody,
                ContentType = responseContentType,
                StatusCode = (int)response.StatusCode
            };
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = $"Could not reach the Catalog API: {ex.Message}" });
        }
        catch (TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { error = "The Catalog API did not respond in time." });
        }
    }
}
