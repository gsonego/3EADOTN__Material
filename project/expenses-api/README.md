# expenses-api

Given to students as-is — no code changes expected or required. Your job is the
infrastructure it runs on (see the project brief, Sections 3 and 3.3) and deploying
this image onto it.

## Hosting

Azure Container Apps (`app-expenses-<your_suffix>`), consumption plan, in a Container
Apps Environment (`env-expenses-<your_suffix>`). Target port **8080** — a mismatch here
doesn't error clearly, the revision just sits in `ActivationFailed`.

## Configuration (environment variables — never appsettings.json)

Both Cosmos DB and Blob Storage accept config "evolving" from the simpler pattern
taught in Module 2 up to the Managed-Identity pattern from Module 3 — the app tries
the more secure option first and falls back to the simpler one, so it works at
whichever stage your infrastructure is at:

| Variable | Purpose | Required for |
|---|---|---|
| `CosmosDb__AccountEndpoint` | Cosmos DB account URI — used with the Container App's Managed Identity | Reading/writing expenses (preferred path) |
| `CosmosDb__ConnectionString` | Plain Cosmos connection string | Reading/writing expenses (fallback, used only if `AccountEndpoint` is unset) |
| `CosmosDb__DatabaseName` | Defaults to `ExpenseTrackerDb` if unset | — |
| `CosmosDb__ContainerName` | Defaults to `Expenses` if unset | — |
| `BlobStorage__AccountName` | Storage account name — paired with the `StorageAccountKey` Key Vault secret | Receipt photo upload (preferred path) |
| `BlobStorage__ConnectionString` | Plain Blob Storage connection string | Receipt photo upload (fallback, used only if `AccountName` + the Key Vault secret aren't both available) |
| `BlobStorage__ContainerName` | Defaults to `receipts` if unset | — |
| `KeyVault__VaultUri` | Key Vault URI holding the `StorageAccountKey` secret | Receipt photo upload (preferred path only) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights ingestion | Monitoring |

Only the preferred path (`CosmosDb__AccountEndpoint` + the Cosmos DB data-plane RBAC
role; `BlobStorage__AccountName` + the Key Vault-held `StorageAccountKey`) satisfies
the project brief's D3 criterion — "Managed Identity used for service-to-service
access... nothing sensitive hard-coded or pasted into app configuration." The
`ConnectionString` variables are a working fallback while your infrastructure is
mid-build, not an equivalent substitute for grading purposes.

## Guard behavior (read before you assume something is "broken")

None of the above are required for the app to **start**. If a dependency isn't
configured or isn't reachable:

- `GET /api/expenses` returns `200` with an empty `items` array and
  `"dataSourceConnected": false` — not an error.
- `POST /api/expenses` returns `503` with a plain-text reason if Cosmos DB isn't
  reachable — the write genuinely didn't happen, but the app doesn't crash.
- `DELETE /api/expenses/{category}/{id}` returns `503` the same way if Cosmos DB isn't
  reachable, `404` if the id doesn't exist, `204` on success.
- The receipt-upload endpoint returns `200` with `"blobConnected": false` and no URL
  if Blob Storage/Key Vault aren't wired up yet — the expense record itself is
  unaffected, since a photo is optional per the brief.

`GET /health` reports `cosmosConfigured` / `blobConfigured` booleans — check this first
when something looks wrong.

## Build & push (once you can restore NuGet packages)

```
docker build -t $ACR/expenses-api:v1 .
docker push $ACR/expenses-api:v1
```

NuGet package versions in `ExpensesApi.csproj` were picked at authoring time without
being able to reach nuget.org from the authoring sandbox — run `dotnet restore` once
on a normal machine and let it confirm/bump them before the first real build.
