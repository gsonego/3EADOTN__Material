using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using catalog_api.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// In-process cache for /titles/count (Module 4, Topic 1). Fine for a single
// App Service/Container App instance -- see the manual's note on why this
// wouldn't be safe as a *shared* cache once the app scales to N replicas.
builder.Services.AddMemoryCache();

// Cosmos requires lowercase "id", and the container's partition key path
// ("/genre") must match the serialized property name -- CamelCase fixes both
// at once, since they're really the same root cause (see Section 1.3).
var cosmosOptions = new CosmosClientOptions
{
    SerializerOptions = new CosmosSerializationOptions
    {
        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
    }
};
// No connection string, no Key Vault -- Cosmos DB is Entra-ID-aware, so the
// Container App's managed identity authenticates directly (Module 3, 2.3).
// DefaultAzureCredential picks up the Container App's managed identity in
// Azure and falls back to `az login`'s credential for local `dotnet run`.
var cosmosClient = new CosmosClient(builder.Configuration["CosmosDb:AccountEndpoint"], new DefaultAzureCredential(), cosmosOptions);
var titlesContainer = cosmosClient.GetContainer(builder.Configuration["CosmosDb:DatabaseName"], builder.Configuration["CosmosDb:ContainerName"]);

var blobContainer = new BlobContainerClient(builder.Configuration["BlobStorage:ConnectionString"], builder.Configuration["BlobStorage:ContainerName"]);

var app = builder.Build();
var memoryCache = app.Services.GetRequiredService<IMemoryCache>();

// We are leaving Swagger on in production for this sample,
// so that you can see the OpenAPI spec and test the endpoints from the browser.
// In a real production app, you would likely want to turn this off
// (or at least lock it down) in production.
// ---------------------------------------------------
// In this project, we are leaving Swagger so we can use it
// when we discuss about Azure APIM configuration in later modules.
// ---------------------------------------------------
//if (app.Environment.IsDevelopment())
//{
app.UseSwagger();
app.UseSwaggerUI();
//}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    app = "catalog-api",
    version = CatalogApiVersion.Value,
    timestamp = DateTimeOffset.UtcNow
}))
.WithName("GetHealth")
.WithOpenApi();

app.MapGet("/titles", async () =>
{
    var results = new List<TitleDto>();
    using var iterator = titlesContainer.GetItemQueryIterator<TitleItem>("SELECT * FROM c");
    while (iterator.HasMoreResults)
    {
        foreach (var item in await iterator.ReadNextAsync())
        {
            results.Add(ToDto(item, blobContainer));
        }
    }
    return Results.Ok(results);
})
.WithName("ListTitles")
.WithOpenApi();

const string TitleCountCacheKey = "titles-count";
var titleCountTtl = TimeSpan.FromSeconds(30);

app.MapGet("/titles/count", async () =>
{
    if (memoryCache.TryGetValue(TitleCountCacheKey, out TitleCountResponse? cached))
    {
        return Results.Ok(cached! with { CacheStatus = "HIT", RequestCharge = 0 });
    }

    // A COUNT aggregate is still a real Cosmos DB query -- it costs RUs like
    // any other read, it just doesn't transfer the item bodies. RequestCharge
    // on the response is what the cache is actually saving on a HIT.
    var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c");
    using var iterator = titlesContainer.GetItemQueryIterator<int>(query);
    var count = 0;
    var requestCharge = 0.0;
    while (iterator.HasMoreResults)
    {
        var page = await iterator.ReadNextAsync();
        requestCharge += page.RequestCharge;
        count = page.FirstOrDefault();
    }

    var result = new TitleCountResponse(count, "MISS", requestCharge, (int)titleCountTtl.TotalSeconds);
    // Writes (POST/PUT/DELETE below) deliberately don't invalidate this --
    // the count stays stale until the TTL expires on its own, same trade-off
    // taught in the manual's caching demo.
    memoryCache.Set(TitleCountCacheKey, result, titleCountTtl);
    return Results.Ok(result);
})
.WithName("GetTitleCount")
.WithOpenApi();

app.MapPost("/titles", async (TitleRequest request) =>
{
    var item = new TitleItem
    {
        Title = request.Title,
        Genre = request.Genre,
        Year = request.Year,
        Description = request.Description
    };
    var created = await titlesContainer.CreateItemAsync(item, new PartitionKey(item.Genre));
    return Results.Created($"/titles/{created.Resource.Id}", ToDto(created.Resource, blobContainer));
})
.WithName("CreateTitle")
.WithOpenApi();

app.MapPut("/titles/{id}", async (string id, TitleRequest request) =>
{
    var existing = await FindById(titlesContainer, id);
    if (existing is null) return Results.NotFound();

    var updated = new TitleItem
    {
        Id = id,
        Title = request.Title,
        Genre = request.Genre,
        Year = request.Year,
        Description = request.Description,
        PosterBlobName = existing.PosterBlobName
    };

    if (existing.Genre == updated.Genre)
    {
        // Same partition -- a plain replace.
        var replaced = await titlesContainer.ReplaceItemAsync(updated, id, new PartitionKey(existing.Genre));
        return Results.Ok(ToDto(replaced.Resource, blobContainer));
    }

    // Genre changed = partition key changed. Cosmos doesn't support an
    // in-place partition key change on Replace, so this is a delete +
    // recreate under the hood -- same item id, new partition.
    await titlesContainer.DeleteItemAsync<TitleItem>(id, new PartitionKey(existing.Genre));
    var created = await titlesContainer.CreateItemAsync(updated, new PartitionKey(updated.Genre));
    return Results.Ok(ToDto(created.Resource, blobContainer));
})
.WithName("UpdateTitle")
.WithOpenApi();

app.MapDelete("/titles/{id}", async (string id) =>
{
    var existing = await FindById(titlesContainer, id);
    if (existing is null) return Results.NotFound();

    await titlesContainer.DeleteItemAsync<TitleItem>(id, new PartitionKey(existing.Genre));
    return Results.NoContent();
})
.WithName("DeleteTitle")
.WithOpenApi();

app.MapPost("/titles/{id}/poster", async (string id, IFormFile file) =>
{
    var existing = await FindById(titlesContainer, id);
    if (existing is null) return Results.NotFound();

    var blobName = $"{id}{Path.GetExtension(file.FileName)}";
    var blobClient = blobContainer.GetBlobClient(blobName);

    await using (var stream = file.OpenReadStream())
    {
        await blobClient.UploadAsync(stream, overwrite: true);
    }

    existing.PosterBlobName = blobName;
    var replaced = await titlesContainer.ReplaceItemAsync(existing, id, new PartitionKey(existing.Genre));
    return Results.Ok(ToDto(replaced.Resource, blobContainer));
})
.WithName("UploadPoster")
.WithOpenApi()
.DisableAntiforgery();

app.Run();

// The route only carries the title's id, not its genre/partition key -- a
// point read needs both, so this falls back to a cross-partition query to
// look the item up first. Costs more RUs than a point read; that's the
// trade-off of not knowing the partition key up front.
static async Task<TitleItem?> FindById(Container container, string id)
{
    var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id);
    using var iterator = container.GetItemQueryIterator<TitleItem>(query, requestOptions: new QueryRequestOptions { MaxItemCount = 1 });
    while (iterator.HasMoreResults)
    {
        var page = await iterator.ReadNextAsync();
        var match = page.FirstOrDefault();
        if (match is not null) return match;
    }
    return null;
}

// Deliberately not cached anywhere -- a fresh, short-lived read-only SAS URL
// is generated on every response, the same "private by default, time-limited
// access" idea taught hands-on in Section 2.2, just happening in application
// code instead of the CLI this time.
static TitleDto ToDto(TitleItem item, BlobContainerClient blobContainer)
{
    string? posterUrl = null;
    if (!string.IsNullOrEmpty(item.PosterBlobName))
    {
        var blobClient = blobContainer.GetBlobClient(item.PosterBlobName);
        if (blobClient.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = blobContainer.Name,
                BlobName = item.PosterBlobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            posterUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
        }
    }
    return new TitleDto(item.Id, item.Title, item.Genre, item.Year, item.Description, posterUrl);
}

static class CatalogApiVersion
{
    // v7: adds the cached /titles/count endpoint (Module 4, Topic 1 -- Caching).
    public const string Value = "v7";
}
