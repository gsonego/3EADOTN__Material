using Newtonsoft.Json;

namespace ExpensesApi.Models;

public class Expense
{
    [JsonProperty("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Also the Cosmos DB partition key value (container "Expenses", partition key /category).
    [JsonProperty("category")]
    public string Category { get; set; } = "Other";

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; } = "EUR";

    [JsonProperty("date")]
    public DateTime Date { get; set; } = DateTime.UtcNow.Date;

    // Null/empty when no receipt photo was attached — optional per record (see brief Section 2/3.2).
    [JsonProperty("receiptPhotoUrl")]
    public string? ReceiptPhotoUrl { get; set; }

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class ExpenseCategories
{
    // Fixed list from the project brief, Section 3.1 — do not add/remove without updating the brief.
    public static readonly string[] All =
    {
        "Groceries", "Entertainment", "Restaurants", "Transport", "Utilities", "Other"
    };

    public static bool IsValid(string category) =>
        All.Contains(category, StringComparer.OrdinalIgnoreCase);
}
