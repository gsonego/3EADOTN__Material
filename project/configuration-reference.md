# CloudExpense — configuration reference

What `expenses-ui` and `expenses-api` need to actually work end to end, grouped by
app and by where the value lives (env var, Key Vault secret, RBAC role, or — for the
UI — a browser-side setting). Source of truth is the code itself
(`expenses-api/Program.cs`, `expenses-api/Services/*.cs`,
`expenses-ui/wwwroot/js/app.js`) and the project brief's Section 3; update this file
if either changes.

## expenses-api (Container App `app-expenses-<suffix>`)

### Environment variables

Set with `az containerapp update --set-env-vars`, using `__` for nested config keys.
Never `appsettings.json` — it stays minimal (`Logging`/`AllowedHosts` only).

Cosmos DB and Blob Storage each accept an "evolving" pair of variables — the app
tries the Managed-Identity path first and falls back to a plain connection string if
that's not set, so it works whether your infra is at the Module 2 stage or the
Module 3 stage:

| Variable                                | Required for             | Notes                                                                                                                                                                        |
| --------------------------------------- | ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CosmosDb__AccountEndpoint`             | Reading/writing expenses | **Preferred.** Cosmos URI, used with Managed Identity — no secret involved                                                                                                   |
| `CosmosDb__ConnectionString`            | Reading/writing expenses | Fallback, only used if `AccountEndpoint` is unset                                                                                                                            |
| `CosmosDb__DatabaseName`                | —                        | Optional, defaults to `ExpenseTrackerDb`                                                                                                                                     |
| `CosmosDb__ContainerName`               | —                        | Optional, defaults to `Expenses`                                                                                                                                             |
| `BlobStorage__AccountName`              | Receipt photo upload     | **Preferred.** Name only — paired with the `StorageAccountKey` Key Vault secret below                                                                                        |
| `BlobStorage__ConnectionString`         | Receipt photo upload     | Fallback, only used if `AccountName` + the Key Vault secret aren't both available                                                                                            |
| `BlobStorage__ContainerName`            | —                        | Optional, defaults to `receipts`                                                                                                                                             |
| `KeyVault__VaultUri`                    | Receipt photo upload     | Needed so the app can fetch `StorageAccountKey` from Key Vault (preferred path only)                                                                                         |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Monitoring               | Flat key, not `ApplicationInsights:ConnectionString` — the SDK silently ignores that alternative. Guarded: the app starts fine without it.                                   |
| `IDENTITY_ENDPOINT`                     | —                        | Don't set this yourself — Container Apps injects it automatically once Managed Identity is enabled; the app uses its presence to pick Managed Identity vs. local `az login`. |

Only the preferred (Managed Identity) path on each satisfies the brief's D3 criterion
— the `ConnectionString` fallbacks are a working stopgap, not an equivalent for
grading purposes.

### Key Vault secret

Not an env var — fetched at runtime via `SecretProvider`.

| Secret name         | Value                                        |
| ------------------- | -------------------------------------------- |
| `StorageAccountKey` | The Blob Storage account's actual access key |

### Managed Identity / RBAC (control-plane — `az role assignment create`)

| Role                             | Scope                        | Why                                                                              |
| -------------------------------- | ---------------------------- | -------------------------------------------------------------------------------- |
| System-assigned Managed Identity | enabled on the Container App | Prerequisite for everything below                                                |
| `Key Vault Secrets User`         | the Key Vault                | Lets the app read `StorageAccountKey`                                            |
| `AcrPull`                        | the ACR                      | Lets the Container App pull its own image (or use ACR admin credentials instead) |

### Cosmos DB data-plane RBAC (separate system — `az cosmosdb sql role assignment create`)

| Role                                  | Scope | Principal                                         |
| ------------------------------------- | ----- | ------------------------------------------------- |
| `Cosmos DB Built-in Data Contributor` | `/`   | The Container App's managed-identity principal ID |

## expenses-ui (App Service `webapp-expenses-<suffix>`)

### Environment variables

| Variable             | Why                                                                                  |
| -------------------- | ------------------------------------------------------------------------------------ |
| `WEBSITES_PORT=8080` | The container listens on 8080, not 80 — App Service won't route traffic without this |

### appsettings.json

Nothing app-specific — config comes from the browser instead (see below).

### Browser-side (Settings modal → `localStorage`, per device, no server config at all)

| Field            | Value                                                                                                                       |
| ---------------- | --------------------------------------------------------------------------------------------------------------------------- |
| Base URL         | The **APIM gateway URL**, e.g. `https://apim-expenses-<suffix>.azure-api.net/expenses` — never the Container App's own FQDN |
| Subscription key | The APIM subscription key for that API                                                                                      |

- NOTE: You can use the Container API FQDN while you don't have APIM setup yet.

## Shared prerequisite: API Management

Neither app's config matters until this exists — it's what the UI's Base URL/Subscription
key actually point at.

- `apim-expenses-<suffix>`, Consumption tier
- An API imported/routed to the Container App's FQDN as backend
- Subscription required on the product — this is what generates the key the UI needs

## Deployment-only (not app runtime config)

ACR (`acrexpenses<suffix>`) holding both images, the Container Apps Environment for the
API, and the B1 App Service Plan for the UI — these just need to exist; no runtime
parameters flow from them into the apps themselves.
