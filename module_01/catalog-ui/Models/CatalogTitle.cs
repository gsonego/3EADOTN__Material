namespace catalog_ui.Models;

// Mirrors the Catalog API's entity shape. The API only exposes GET / (health)
// at the Module 1 stage -- these fields are what later modules' CRUD endpoints
// will return once Cosmos DB is wired up.
public record CatalogTitle
{
    public string? Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;
    public int Year { get; init; }
    public string? Description { get; init; }
    public string? PosterUrl { get; init; }
}

public static class CatalogGenres
{
    public static readonly string[] All = ["Action", "Comedy", "Documentary", "Drama", "Sci-Fi", "Other"];
}
