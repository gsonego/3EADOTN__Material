using Azure.Core;
using ExpensesApi.Models;
using Microsoft.Azure.Cosmos;

namespace ExpensesApi.Services;

/// <summary>
/// Guarded wrapper around Cosmos DB (database "ExpenseTrackerDb", container "Expenses",
/// partition key /category — see brief Section 3.1). Every method degrades to an
/// empty/failed result with Connected=false instead of throwing, so a student's
/// half-provisioned deployment shows an honest "not connected" state rather than a
/// crash or, worse, silently-faked data.
///
/// Auth is "evolving": CosmosDb:AccountEndpoint + Managed Identity (the Module 3
/// end state — no secret at all) is preferred and tried first; CosmosDb:ConnectionString
/// (the Module 2 pattern) is a fallback for students who haven't wired up the Managed
/// Identity + RBAC data-plane role assignment yet. Either gets the app working; only
/// the AccountEndpoint path satisfies the brief's D3 "Managed Identity used for
/// service-to-service access" criterion.
/// </summary>
public class CosmosExpenseStore
{
    private readonly ILogger<CosmosExpenseStore> _logger;
    private readonly Container? _container;

    public bool IsConfigured { get; }

    public CosmosExpenseStore(IConfiguration config, TokenCredential credential, ILogger<CosmosExpenseStore> logger)
    {
        _logger = logger;
        var endpoint = config["CosmosDb:AccountEndpoint"];
        var connectionString = config["CosmosDb:ConnectionString"];
        var dbName = config["CosmosDb:DatabaseName"] ?? "ExpenseTrackerDb";
        var containerName = config["CosmosDb:ContainerName"] ?? "Expenses";

        if (string.IsNullOrWhiteSpace(endpoint) && string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("Neither CosmosDb:AccountEndpoint nor CosmosDb:ConnectionString is set — running without a data store; reads return empty, writes are rejected.");
            IsConfigured = false;
            return;
        }

        try
        {
            var client = !string.IsNullOrWhiteSpace(endpoint)
                ? new CosmosClient(endpoint, credential)
                : new CosmosClient(connectionString);
            _container = client.GetContainer(dbName, containerName);
            IsConfigured = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the Cosmos DB client — running without a data store.");
            IsConfigured = false;
        }
    }

    public async Task<(IReadOnlyList<Expense> Items, bool Connected)> ListAsync(string? category)
    {
        if (!IsConfigured || _container is null) return (Array.Empty<Expense>(), false);

        try
        {
            var query = category is null
                ? new QueryDefinition("SELECT * FROM c ORDER BY c.date DESC")
                : new QueryDefinition("SELECT * FROM c WHERE c.category = @category ORDER BY c.date DESC")
                    .WithParameter("@category", category);

            var results = new List<Expense>();
            using var iterator = _container.GetItemQueryIterator<Expense>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync();
                results.AddRange(page);
            }
            return (results, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cosmos DB query failed — returning an empty result instead of failing the request.");
            return (Array.Empty<Expense>(), false);
        }
    }

    public async Task<(Expense? Item, bool Connected, string? Error)> CreateAsync(Expense expense)
    {
        if (!IsConfigured || _container is null)
            return (null, false, "Cosmos DB is not connected yet — the expense was not saved.");

        try
        {
            var response = await _container.CreateItemAsync(expense, new PartitionKey(expense.Category));
            return (response.Resource, true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cosmos DB write failed.");
            return (null, false, "Could not save the expense — Cosmos DB is unreachable.");
        }
    }

    public async Task<(Expense? Item, bool Connected)> GetAsync(string id, string category)
    {
        if (!IsConfigured || _container is null) return (null, false);

        try
        {
            var response = await _container.ReadItemAsync<Expense>(id, new PartitionKey(category));
            return (response.Resource, true);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (null, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cosmos DB read failed for expense {Id}.", id);
            return (null, false);
        }
    }

    public async Task<bool> UpdateReceiptUrlAsync(string id, string category, string receiptUrl)
    {
        if (!IsConfigured || _container is null) return false;

        try
        {
            var patch = new[] { PatchOperation.Set("/receiptPhotoUrl", receiptUrl) };
            await _container.PatchItemAsync<Expense>(id, new PartitionKey(category), patch);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to attach the receipt URL to expense {Id}.", id);
            return false;
        }
    }

    public async Task<(bool Deleted, bool Connected)> DeleteAsync(string id, string category)
    {
        if (!IsConfigured || _container is null) return (false, false);

        try
        {
            await _container.DeleteItemAsync<Expense>(id, new PartitionKey(category));
            return (true, true);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return (false, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cosmos DB delete failed for expense {Id}.", id);
            return (false, false);
        }
    }
}
