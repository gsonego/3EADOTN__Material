var builder = WebApplication.CreateBuilder(args);

// Same pattern as catalog-ui (Module 1): the browser never talks to the Expenses API
// (or its APIM gateway) directly. It only calls this server's own same-origin /proxy/*
// routes, sending the base URL + subscription key it saved from the Settings panel as
// custom request headers (X-Expenses-Base-Url / X-Expenses-Api-Key). This server reads
// those per request and adds the subscription key to the outbound call as
// Ocp-Apim-Subscription-Key, so it's never exposed to the browser or reachable via CORS.
builder.Services.AddHttpClient("ExpensesApi");

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

const string BaseUrlHeader = "X-Expenses-Base-Url";
const string ApiKeyHeader = "X-Expenses-Api-Key";
const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";

string[] FallbackCategories =
    { "Groceries", "Entertainment", "Restaurants", "Transport", "Utilities", "Other" };

static string? BaseUrl(HttpRequest request) =>
    request.Headers[BaseUrlHeader].ToString() is { Length: > 0 } v ? v : null;

static HttpClient ApiClient(IHttpClientFactory factory, HttpRequest request, string baseUrl)
{
    var client = factory.CreateClient("ExpensesApi");
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

    var key = request.Headers[ApiKeyHeader].ToString();
    if (!string.IsNullOrWhiteSpace(key))
        client.DefaultRequestHeaders.TryAddWithoutValidation(SubscriptionKeyHeader, key);

    return client;
}

app.MapGet("/proxy/health", async (HttpRequest request, IHttpClientFactory factory) =>
{
    var baseUrl = BaseUrl(request);
    if (baseUrl is null)
        return Results.Json(new { error = "No API endpoint configured. Open Settings and set a base URL." }, statusCode: StatusCodes.Status400BadRequest);

    var client = ApiClient(factory, request, baseUrl);
    try
    {
        var response = await client.GetAsync("health");
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = $"Could not reach the API: {ex.Message}" }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException)
    {
        return Results.Json(new { error = "The API did not respond in time." }, statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/proxy/categories", async (HttpRequest request, IHttpClientFactory factory) =>
{
    var baseUrl = BaseUrl(request);
    if (baseUrl is null) return Results.Ok(FallbackCategories);

    var client = ApiClient(factory, request, baseUrl);
    try
    {
        var response = await client.GetAsync("api/categories");
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch
    {
        return Results.Ok(FallbackCategories);
    }
});

app.MapGet("/proxy/expenses", async (HttpRequest request, IHttpClientFactory factory, string? category) =>
{
    var baseUrl = BaseUrl(request);
    if (baseUrl is null)
        return Results.Ok(new { items = Array.Empty<object>(), dataSourceConnected = false, message = "No API endpoint configured yet. Open Settings and set a base URL." });

    var client = ApiClient(factory, request, baseUrl);
    var path = category is null ? "api/expenses" : $"api/expenses?category={Uri.EscapeDataString(category)}";

    try
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Ok(new { items = Array.Empty<object>(), dataSourceConnected = false, message = $"Could not reach the API: {ex.Message}" });
    }
});

app.MapPost("/proxy/expenses", async (HttpRequest request, IHttpClientFactory factory) =>
{
    var baseUrl = BaseUrl(request);
    if (baseUrl is null)
        return Results.Json(new { error = "No API endpoint configured yet. Open Settings and set a base URL." }, statusCode: StatusCodes.Status503ServiceUnavailable);

    var client = ApiClient(factory, request, baseUrl);

    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync();
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

    try
    {
        var response = await client.PostAsync("api/expenses", content);
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not reach the API: {ex.Message}" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapDelete("/proxy/expenses/{category}/{id}", async (string category, string id, HttpRequest request, IHttpClientFactory factory) =>
{
    var baseUrl = BaseUrl(request);
    if (baseUrl is null)
        return Results.Json(new { error = "No API endpoint configured yet. Open Settings and set a base URL." }, statusCode: StatusCodes.Status503ServiceUnavailable);

    var client = ApiClient(factory, request, baseUrl);
    try
    {
        var response = await client.DeleteAsync($"api/expenses/{category}/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return Results.NoContent();

        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = $"Could not reach the API: {ex.Message}" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/proxy/expenses/{category}/{id}/receipt", async (string category, string id, HttpRequest request, IHttpClientFactory factory) =>
{
    var baseUrl = BaseUrl(request);
    if (baseUrl is null)
        return Results.Ok(new { receiptPhotoUrl = (string?)null, blobConnected = false, message = "No API endpoint configured yet. Open Settings and set a base URL." });

    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Send the photo as multipart/form-data." });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("photo");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No photo file received." });

    var client = ApiClient(factory, request, baseUrl);

    using var multipart = new MultipartFormDataContent();
    await using var stream = file.OpenReadStream();
    using var streamContent = new StreamContent(stream);
    streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
    multipart.Add(streamContent, "photo", file.FileName);

    try
    {
        var response = await client.PostAsync($"api/expenses/{category}/{id}/receipt", multipart);
        var body = await response.Content.ReadAsStringAsync();
        return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        return Results.Ok(new { receiptPhotoUrl = (string?)null, blobConnected = false, message = $"Could not reach the API: {ex.Message}" });
    }
});

app.Run();
