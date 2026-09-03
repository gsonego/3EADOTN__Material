using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace ExpensesApi.Services;

/// <summary>
/// Guarded wrapper around Key Vault. Used for exactly one secret in this app —
/// "StorageAccountKey" (see ReceiptBlobService) — matching the Module 3 pattern:
/// Managed Identity + RBAC (Key Vault Secrets User) to read it, nothing hard-coded.
/// </summary>
public class SecretProvider
{
    private readonly ILogger<SecretProvider> _logger;
    private readonly SecretClient? _client;

    public bool IsConfigured { get; }

    public SecretProvider(IConfiguration config, TokenCredential credential, ILogger<SecretProvider> logger)
    {
        _logger = logger;
        var vaultUri = config["KeyVault:VaultUri"];

        if (string.IsNullOrWhiteSpace(vaultUri))
        {
            _logger.LogWarning("KeyVault:VaultUri is not set — secrets (e.g. the storage account key) are unavailable.");
            IsConfigured = false;
            return;
        }

        try
        {
            _client = new SecretClient(new Uri(vaultUri), credential);
            IsConfigured = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize the Key Vault client.");
            IsConfigured = false;
        }
    }

    public async Task<string?> TryGetSecretAsync(string name)
    {
        if (!IsConfigured || _client is null) return null;

        try
        {
            var secret = await _client.GetSecretAsync(name);
            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read secret '{Name}' from Key Vault.", name);
            return null;
        }
    }
}
