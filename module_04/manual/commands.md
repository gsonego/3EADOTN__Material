# Module 4 — Commands

## 1.2 Live demo — caching GET /titles/count on the Catalog API

```
dotnet build
```

```
dotnet run --urls http://localhost:5097
```

```
curl http://localhost:5097/titles/count
```

Response (MISS — real Cosmos DB query just ran):

```json
{ "count": 8, "cacheStatus": "MISS", "requestCharge": 2.89, "ttlSeconds": 30 }
```

```
curl http://localhost:5097/titles/count
```

Response (HIT — served from `IMemoryCache`, no query ran):

```json
{ "count": 8, "cacheStatus": "HIT", "requestCharge": 0, "ttlSeconds": 30 }
```

```
az acr login --name $ACR
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v7" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v7"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v7" --revision-suffix v7
```

```
curl "https://app-estiam-dev-2--v7.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io/titles/count"
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--v7=100
```

```
curl "https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io/titles/count"
```

Response:

```json
{ "count": 8, "cacheStatus": "HIT", "requestCharge": 0, "ttlSeconds": 30 }
```

## 1.3 Live demo — showing the count in the Catalog UI

No `catalog-ui` build/push/deploy needed here — the count-pill code has lived in the running `catalog-ui:v1` container since Module 1 (grown-in-place project, no per-module folder), just idle until `catalog-api` had `/titles/count` to call.

Browser check at `https://webapp-estiam-dev-2.azurewebsites.net`, endpoint pointed at `https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io` — count pill rendered `8 titles`, HIT (green), confirming the full chain: `catalog-ui` (App Service, unchanged since v1) → `catalog-api-v7` (Container Apps) → Cosmos DB, with the cache visibly working end to end.

## 2.2 Live demo — instrumenting catalog-api

```
$LAW = "law-estiam-appi-dev-2"
```

```
az monitor log-analytics workspace create --resource-group $RG --workspace-name $LAW --location $LOCATION
```

```
$workspaceId = az monitor log-analytics workspace show --resource-group $RG --workspace-name $LAW --query id --output tsv
```

```
$APPI = "appi-estiam-dev-2"
```

```
az monitor app-insights component create --app $APPI --resource-group $RG --location westus2 --workspace $workspaceId
```

`westcentralus` (the course's default region) failed with `LocationNotAvailableForResourceType` — `microsoft.insights/components` isn't offered there. Retried with `westus2`, which worked; the component doesn't need to share a region with its workspace.

Also hit the Git Bash `/subscriptions/...` path-mangling gotcha on this same command — re-run with `MSYS_NO_PATHCONV=1` prefixed (or from PowerShell) fixed it.

```
$connString = az monitor app-insights component show --app $APPI --resource-group $RG --query connectionString --output tsv
```

```
dotnet build
```

```
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = $connString
```

```
dotnet run --urls http://localhost:5097
```

```
curl http://localhost:5097/titles/count
```

```
curl http://localhost:5097/titles/count?fail=true
```

```
az acr login --name $ACR
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v8" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v8"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v8" --revision-suffix v8 --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=$connString"
```

```
curl "https://app-estiam-dev-2--v8.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io/titles/count"
```

```json
{ "count": 8, "cacheStatus": "MISS", "requestCharge": 2.89, "ttlSeconds": 30 }
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--v8=100
```

## 2.3 Live demo — instrumenting catalog-ui

`catalog-ui` already carries the `Azure.Monitor.OpenTelemetry.AspNetCore` wiring (added to the shared, grown-in-place project the same time as `catalog-api-v8`'s instrumentation; `catalog-ui` has no `DefaultAzureCredential`/Cosmos dependency, so the credential gotcha above never applied here). It's been silently inert — `UseAzureMonitor()` only activates once `APPLICATIONINSIGHTS_CONNECTION_STRING` is present — so no new `catalog-ui` build, push, or deploy is needed, only the connection string as an app setting:

```
$connString = az monitor app-insights component show --app $APPI --resource-group $RG --query connectionString --output tsv
```

```
az webapp config appsettings set --name $WEBAPP --resource-group $RG --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=$connString"
```

```
az webapp restart --name $WEBAPP --resource-group $RG
```

```
curl "https://webapp-estiam-dev-2.azurewebsites.net/api/titles/count" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io"
```
