using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using catalog_api.Models;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
var cosmosClient = new CosmosClient(builder.Configuration["CosmosDb:ConnectionString"], cosmosOptions);
var titlesContainer = cosmosClient.GetContainer(builder.Configuration["CosmosDb:DatabaseName"], builder.Configuration["CosmosDb:ContainerName"]);

var blobContainer = new BlobContainerClient(builder.Configuration["BlobStorage:ConnectionString"], builder.Configuration["BlobStorage:ContainerName"]);

var app = builder.Build();

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
    // v5: app code is unchanged from v4 (still reads CosmosDb:ConnectionString
    // as a plain setting). What changes is how that setting gets its value at
    // deploy time: the Container App resolves it via a Key Vault reference
    // (secretref/keyvaultref) through the app's managed identity instead of a
    // literal connection string in --set-env-vars (Module 3, 2.2). The
    // migration to reading the endpoint directly and authenticating with
    // DefaultAzureCredential -- dropping the connection string from config
    // entirely -- happens in v6, not here.
    public const string Value = "v5";
}
