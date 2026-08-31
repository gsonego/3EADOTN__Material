var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// Module 1 skeleton: just proves the app is deployed and reachable.
// Cosmos DB / Blob Storage / Managed Identity / Caching land in later modules.
app.MapGet("/", () => Results.Ok(new
{
    app = "catalog-api",
    version = CatalogApiVersion.Value,
    timestamp = DateTimeOffset.UtcNow
}))
.WithName("GetHealth")
.WithOpenApi();

app.Run();

static class CatalogApiVersion
{
    // Bumped for the Section 5 revisions/traffic-splitting demo (v1 -> v2).
    public const string Value = "v1";
}
