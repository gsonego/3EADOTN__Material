using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace ExpensesApi.Services;

/// <summary>
/// Guarded wrapper around Blob Storage (container "receipts" — see brief Section 3.2).
/// Receipt photos are optional per expense record, but this capability itself is
/// mandatory infra: if it isn't wired up, upload calls degrade to
/// Connected=false rather than throwing or failing the whole expense-creation flow.
///
/// Auth is "evolving", same as CosmosExpenseStore: BlobStorage:AccountName + the
/// "StorageAccountKey" Key Vault secret (fetched via Managed Identity, the Module 3
/// pattern) is preferred and tried first; BlobStorage:ConnectionString (the Module 2
/// pattern — the key pasted straight into the env var, no Key Vault) is a fallback for
/// students who haven't wired up Key Vault yet. Either gets uploads working; only the
/// Key Vault path keeps the raw key out of app configuration, per the brief's D3
/// criterion.
/// </summary>
public class ReceiptBlobService
{
    private readonly ILogger<ReceiptBlobService> _logger;
    private readonly string _containerName;
    private BlobContainerClient? _container;

    public bool IsConfigured { get; }

    public ReceiptBlobService(IConfiguration config, SecretProvider secrets, ILogger<ReceiptBlobService> logger)
    {
        _logger = logger;
        var accountName = config["BlobStorage:AccountName"];
        var connectionString = config["BlobStorage:ConnectionString"];
        _containerName = config["BlobStorage:ContainerName"] ?? "receipts";

        var accountKey = string.IsNullOrWhiteSpace(accountName)
            ? null
            : secrets.TryGetSecretAsync("StorageAccountKey").GetAwaiter().GetResult();

        if (string.IsNullOrWhiteSpace(accountKey) && string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning("No usable Blob Storage config found — set BlobStorage:AccountName (plus the 'StorageAccountKey' Key Vault secret) or BlobStorage:ConnectionString. Receipt photo upload is disabled.");
            IsConfigured = false;
            return;
        }

        try
        {
            var serviceClient = !string.IsNullOrWhiteSpace(accountKey)
                ? new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), new StorageSharedKeyCredential(accountName, accountKey))
                : new BlobServiceClient(connectionString);
            _container = serviceClient.GetBlobContainerClient(_containerName);
            IsConfigured = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the Blob Storage client — receipt photo upload is disabled.");
            IsConfigured = false;
        }
    }

    public async Task<(string? Url, bool Connected)> UploadReceiptAsync(string expenseId, Stream content, string contentType)
    {
        if (!IsConfigured || _container is null) return (null, false);

        try
        {
            await _container.CreateIfNotExistsAsync();
            var blobName = $"{expenseId}.jpg";
            var blobClient = _container.GetBlobClient(blobName);
            await blobClient.UploadAsync(content, overwrite: true);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _containerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return (sasUri.ToString(), true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Receipt upload failed for expense {Id}.", expenseId);
            return (null, false);
        }
    }
}
