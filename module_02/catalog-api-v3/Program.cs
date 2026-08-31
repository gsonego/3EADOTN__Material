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
            results.Add(ToDto(item));
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
    return Results.Created($"/titles/{created.Resource.Id}", ToDto(created.Resource));
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
        return Results.Ok(ToDto(replaced.Resource));
    }

    // Genre changed = partition key changed. Cosmos doesn't support an
    // in-place partition key change on Replace, so this is a delete +
    // recreate under the hood -- same item id, new partition.
    await titlesContainer.DeleteItemAsync<TitleItem>(id, new PartitionKey(existing.Genre));
    var created = await titlesContainer.CreateItemAsync(updated, new PartitionKey(updated.Genre));
    return Results.Ok(ToDto(created.Resource));
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

// Poster upload/SAS URLs land in v4 (Section 2.4) -- for now PosterUrl is
// always null, matching v2's static list which never had real posters either.
static TitleDto ToDto(TitleItem item) => new(item.Id, item.Title, item.Genre, item.Year, item.Description, PosterUrl: null);

static class CatalogApiVersion
{
    // v3: real Cosmos DB-backed persistence replaces v2's static in-memory list.
    // Blob Storage poster uploads land in v4 (Section 2.4) -- deployed and
    // tested separately so each new capability gets its own in-class demo.
    public const string Value = "v3";
}
