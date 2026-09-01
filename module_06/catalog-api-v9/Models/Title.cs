namespace catalog_api.Models;

// The Cosmos DB item shape. Partition key is Genre ("/genre") -- see the
// manual's Section 1.1 note on why that's an illustrative, not production,
// choice. PosterBlobName is internal: the API never hands this out directly,
// it generates a fresh SAS URL from it on every read (see Section 2.4).
public class TitleItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Genre { get; set; } = "Other";
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
    public string? PosterBlobName { get; set; }
}

// The shape the Catalog UI actually consumes over HTTP (see catalog.js) --
// PosterUrl is a computed, time-limited SAS URL, never the raw blob name.
public record TitleDto(string Id, string Title, string Genre, int Year, string? Description, string? PosterUrl);

public record TitleRequest(string Title, string Genre, int Year, string? Description);

// Response shape for the cached /titles/count endpoint (Module 4, Topic 1).
// RequestCharge is the real Cosmos DB RU cost of the query that produced this
// count -- 0 on a cache HIT, since no query ran. CacheStatus lets the UI show
// the MISS/HIT distinction directly instead of inferring it from timing.
public record TitleCountResponse(int Count, string CacheStatus, double RequestCharge, int TtlSeconds);
