using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using ExpensesApi.Models;
using ExpensesApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Application Insights — reads APPLICATIONINSIGHTS_CONNECTION_STRING automatically
// (this exact env var name; the "obvious" ApplicationInsights:ConnectionString config
// key is silently ignored by UseAzureMonitor()). Guarded like every other integration
// below: UseAzureMonitor() has no built-in tolerance for a missing connection string —
// unlike Cosmos/Blob/Key Vault it fails fast during host startup, and because the
// generic host eagerly constructs hosted services during builder.Build(), that throw
// crashes the whole app (confirmed live: a Container App with no env vars set yet
// crash-loops on this line alone). Only wire it up once a connection string exists.
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

// DefaultAzureCredential, with the local-dev fix for a known interaction: adding
// Azure.Monitor.OpenTelemetry.AspNetCore bumps Azure.Core to a version whose failed
// Managed Identity probe throws instead of falling through to `az login` locally.
// IDENTITY_ENDPOINT is set by Container Apps/App Service whenever Managed Identity is
// really available, so its absence is a reliable "not actually running in Azure" signal.
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeManagedIdentityCredential = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT") is null
});
builder.Services.AddSingleton<TokenCredential>(credential);

builder.Services.AddSingleton<SecretProvider>();
builder.Services.AddSingleton<CosmosExpenseStore>();
builder.Services.AddSingleton<ReceiptBlobService>();

// Exposed through API Management (see brief Section 3.3) — CORS is left open here since
// APIM itself is the access boundary; the UI never calls this API directly.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();

app.MapGet("/health", (CosmosExpenseStore store, ReceiptBlobService blobs) => Results.Ok(new
{
    status = "ok",
    cosmosConfigured = store.IsConfigured,
    blobConfigured = blobs.IsConfigured,
}));

app.MapGet("/api/categories", () => Results.Ok(ExpenseCategories.All));

app.MapGet("/api/expenses", async (CosmosExpenseStore store, string? category) =>
{
    if (category is not null && !ExpenseCategories.IsValid(category))
        return Results.BadRequest(new { error = $"Unknown category '{category}'. Valid values: {string.Join(", ", ExpenseCategories.All)}" });

    var (items, connected) = await store.ListAsync(category);
    return Results.Ok(new
    {
        items,
        dataSourceConnected = connected,
        message = connected ? null : "Cosmos DB is not connected yet — no expenses to show."
    });
});

app.MapGet("/api/expenses/{category}/{id}", async (string category, string id, CosmosExpenseStore store) =>
{
    var (item, connected) = await store.GetAsync(id, category);
    if (!connected)
        return Results.Ok(new { item = (Expense?)null, dataSourceConnected = false, message = "Cosmos DB is not connected yet." });
    return item is null ? Results.NotFound() : Results.Ok(new { item, dataSourceConnected = true });
});

app.MapPost("/api/expenses", async (ExpenseInput input, CosmosExpenseStore store) =>
{
    if (!ExpenseCategories.IsValid(input.Category))
        return Results.BadRequest(new { error = $"Unknown category '{input.Category}'. Valid values: {string.Join(", ", ExpenseCategories.All)}" });

    if (input.Amount <= 0)
        return Results.BadRequest(new { error = "Amount must be greater than zero." });

    var expense = new Expense
    {
        Category = input.Category,
        Description = input.Description ?? string.Empty,
        Amount = input.Amount,
        Date = input.Date ?? DateTime.UtcNow.Date,
    };

    var (created, connected, error) = await store.CreateAsync(expense);
    if (!connected)
        return Results.Json(new { error }, statusCode: StatusCodes.Status503ServiceUnavailable);

    return Results.Created($"/api/expenses/{created!.Category}/{created.Id}", new { item = created });
});

app.MapDelete("/api/expenses/{category}/{id}", async (string category, string id, CosmosExpenseStore store) =>
{
    var (deleted, connected) = await store.DeleteAsync(id, category);
    if (!connected)
        return Results.Json(new { error = "Cosmos DB is not connected yet — nothing was deleted." }, statusCode: StatusCodes.Status503ServiceUnavailable);

    return deleted ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/expenses/{category}/{id}/receipt", async (string category, string id, HttpRequest request, CosmosExpenseStore store, ReceiptBlobService blobs) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Send the photo as multipart/form-data." });

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("photo");
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No photo file received." });

    await using var stream = file.OpenReadStream();
    var (url, blobConnected) = await blobs.UploadReceiptAsync(id, stream, file.ContentType);

    if (!blobConnected)
        return Results.Ok(new { receiptPhotoUrl = (string?)null, blobConnected = false, message = "Photo not stored — Blob Storage is not connected yet. The expense record itself is unaffected." });

    var saved = await store.UpdateReceiptUrlAsync(id, category, url!);
    return Results.Ok(new { receiptPhotoUrl = url, blobConnected = true, savedOnExpense = saved });
});

app.Run();

record ExpenseInput(string Category, string? Description, decimal Amount, DateTime? Date);
