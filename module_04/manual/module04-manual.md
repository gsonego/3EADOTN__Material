# Module 4 — Monitor, Troubleshoot and Optimize Azure Solutions

Modern Enterprise Software Engineering — Day 2, Morning.

This module continues directly in the shared course resource group from Modules 1–3 — no new resource group is created. If you're picking the course back up on a new day, re-run `materials/variables.ps1` before continuing. Confirm the Catalog app chain (`catalog-api-v6`/`catalog-ui`) is still up before starting — this module builds directly on it. Also confirm Docker Desktop is running before class — both topics in this module need a local `docker build`.

---

## 1. Topic 1 — Caching

### 1.1 Concept summary

Caching is a bet on the _rate of change_ of your data, not its cost to compute. If data changes every second, caching just serves stale garbage; if it barely changes, caching trades a small amount of staleness for a large amount of speed and cost saved.

Every Cosmos DB read consumes Request Units (RUs) — real, metered cost, on top of latency. `catalog-api`'s `GET /titles` list has stayed deliberately uncached through Modules 2 and 3 so that this module could introduce caching as a new, explicit idea rather than something quietly baked in from the start.

**Where should the cache live?** This is the key design question, and the answer depends on the host:

|                                        | In-process (`IMemoryCache`)                                             | Distributed (Azure Cache for Redis)                                                                               |
| -------------------------------------- | ----------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------- |
| **Shared across instances?**           | No — each instance has its own separate cache in its own process memory | Yes — one shared cache, all instances see the same data                                                           |
| **Survives a restart / new revision?** | No — wiped completely                                                   | Yes — lives outside the app entirely                                                                              |
| **New Azure resource needed?**         | No                                                                      | Yes — provisioning, RBAC, cost                                                                                    |
| **AZ-204's official answer**           | Not the exam's focus                                                    | Yes — "Configure cache and expiration policies for Azure Cache for Redis" is literally in the exam skills outline |

`catalog-api` runs on Container Apps, which can scale out to multiple replicas under load (its scale rule allows up to 10). That makes `IMemoryCache` a genuinely useful _per-replica_ optimization, but **not** a shared one. Today's live demo uses `IMemoryCache` anyway, for exactly that reason: it's the right tool for a single value that's cheap to be briefly inconsistent about, and the trade-off is worth stating plainly rather than hidden.

### 1.2 Live demo — caching `GET /titles/count` on the Catalog API

`catalog-api-v7` is already set up with a new endpoint, `GET /titles/count`, that runs a real Cosmos DB `COUNT` aggregate query and caches the result in `IMemoryCache` for 30 seconds. The response carries `cacheStatus` (`HIT`/`MISS`) and `requestCharge` (the real RU cost of the query) — a MISS shows students exactly what the cache is saving them, instead of an artificial delay standing in for it. Writes (`POST`/`PUT`/`DELETE /titles`) deliberately do **not** invalidate the cached count — it stays stale until the TTL expires on its own, the same trade-off called out in 1.1.

Logs in to the ACR (Docker Desktop must be running) and builds/pushes the image, run from `catalog-api-v7/`:

```powershell
az acr login --name $ACR
```

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v7" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v7"
```

Deploys the new revision:

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v7" --revision-suffix v7
```

Open the new revision's own FQDN in the browser, at `/titles/count`, twice — the first load is a MISS with a real `requestCharge` (e.g. `2.89`); reload immediately after and it's a HIT with `requestCharge` 0. The RU charge on the MISS is what the cache is saving you from paying on every request. This confirms the new code works in Azure before a single production request touches it.

Moves traffic to the new revision:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--v7=100
```

Open `/titles/count` on the production URL — same response, now live.

### 1.3 Live demo — showing the count in the Catalog UI

`catalog-ui` already has a pill for this in its top bar (next to the existing endpoint pill), showing the live title count with a colored dot: **green** for a cache HIT, **amber** for a MISS. Its tooltip shows the real RU cost. Unlike `catalog-api`, `catalog-ui` doesn't get a new versioned project folder — it's a single app that's grown in place since Module 1, and the pill's code has lived in that one folder since the `catalog-ui:v1` image deployed in Module 1. It's been idle until now (`-- titles`, neutral dot — the fetch fails gracefully because `catalog-api` had nothing at `/titles/count` yet), not newly added today.

**No new `catalog-ui` build, push, or deploy is needed for this step.** The already-running container already has the pill's JS and its `/api/titles/count` proxy route; the only thing missing was `catalog-api` having something to serve, which the previous step just fixed.

Browse `https://$WEBAPP.azurewebsites.net` — the count pill should now show `8 titles` (or however many exist) with a colored dot, and the grid should load real posters. This is the whole lesson made visible end-to-end: add a title, watch the grid update immediately but the count stay stale for up to 30 seconds.

#### Issues & Fixes — Topic 1

- **`catalog-ui` needed no rebuild for this topic.** Its count-pill code has lived in the shared, grown-in-place project since Module 1 — there's no separate `catalog-ui`-per-module folder the way `catalog-api` has. It was simply dormant until `catalog-api` had `/titles/count` to call. Confirmed: the already-deployed `catalog-ui:v1` container picks it up on the very next page load, no redeploy required.

## 2. Topic 2 — Application Insights

### 2.1 Concept summary

Without Application Insights, a student debugging a failure has only `dotnet run`'s console, or `az containerapp logs show` — and only sees whatever they thought to log ahead of time. If the failure is inside a dependency call (Cosmos, Blob), they get a generic 500 with no idea which back-end actually broke.

Application Insights auto-captures three things the moment an app is instrumented, with **zero extra code**:

| Signal           | What it is                                                                                                        |
| ---------------- | ----------------------------------------------------------------------------------------------------------------- |
| **Requests**     | Every incoming call: which one, how long, success/fail.                                                           |
| **Dependencies** | Every _outbound_ call your code makes — Cosmos, Blob, outbound HTTP. Timed and marked success/fail automatically. |
| **Exceptions**   | Any unhandled exception, full stack trace, tied to the request that threw it.                                     |

One more signal, opt-in but zero extra code once wired: **Traces** — confusingly named, this is just `ILogger.LogInformation`/`LogWarning`/`LogError` calls, shipped to App Insights instead of only the console.

Everything shares an **Operation Id**, so App Insights can stitch one failed request + its dependency calls + its exceptions + its log lines into a single end-to-end timeline. `catalog-ui` and `catalog-api` are two _separate_ apps, but because both get instrumented today, a single browser action shows up as **one** correlated Operation Id spanning both — `catalog-ui`'s incoming request, its outbound HTTP call to `catalog-api` as a Dependency, and `catalog-api`'s own Cosmos DB dependency underneath that. That correlated cross-service view is the actual payoff.

### 2.2 Live demo — instrumenting `catalog-api`

`catalog-api-v8` is already set up with OpenTelemetry instrumentation, the Cosmos tracing fix (see the Issues & Fixes note below), and a `?fail=true` demo hook on `/titles/count` used to generate a deliberate failure. A new Log Analytics workspace and Application Insights component are created first — dedicated to this module, deliberately separate from the Container Apps environment's own auto-generated workspace from Module 1.

Sets the workspace name and creates it — modern App Insights is workspace-based, it needs this behind it for storage; App Insights itself is more of a query/view layer on top:

```powershell
$LAW = "law-estiam-appi-dev-2"
```

```powershell
az monitor log-analytics workspace create --resource-group $RG --workspace-name $LAW --location $LOCATION
```

Stores the workspace resource id, needed for the next step:

```powershell
$workspaceId = az monitor log-analytics workspace show --resource-group $RG --workspace-name $LAW --query id --output tsv
```

Sets the App Insights component name and creates it, linked to that workspace. **`$LOCATION` (westcentralus) doesn't work here** — `microsoft.insights/components` isn't offered in every region; confirmed live with `LocationNotAvailableForResourceType`. The component doesn't need to share a region with its workspace — `westus2` is used instead, for this one resource only:

```powershell
$APPI = "appi-estiam-dev-2"
```

```powershell
az monitor app-insights component create --app $APPI --resource-group $RG --location westus2 --workspace $workspaceId
```

Stores the connection string:

```powershell
$connString = az monitor app-insights component show --app $APPI --resource-group $RG --query connectionString --output tsv
```

Logs in and builds/pushes the image, run from `catalog-api-v8/`:

```powershell
az acr login --name $ACR
```

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v8" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v8"
```

Deploys, with the connection string wired in as an environment variable:

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v8" --revision-suffix v8 --set-env-vars "APPLICATIONINSIGHTS_CONNECTION_STRING=$connString"
```

Open the new revision's own FQDN in the browser at `/titles/count` a few times, then at `/titles/count?fail=true` — the first calls succeed normally, the last one returns a `500` from the deliberate demo hook. This generates a realistic mix of telemetry without needing to actually break Cosmos DB, and confirms Managed Identity works before any production traffic touches the new code.

In the Portal: `appi-estiam-dev-2` → **Transaction Search** → a successful `GET /titles/count`. The end-to-end transaction view should show the request, a nested **cosmosdb**-type dependency (`query_items Titles`), and a **Trace** log line — if the Cosmos dependency is missing, see the Issues & Fixes note below, don't skip it.

Click the failed (`?fail=true`) trace — the request should be marked failed (500), plus an **Exception** (`InvalidOperationException`, full stack trace) under the same Operation Id. This is the "student isn't blind anymore" moment for the deck.

Moves traffic to `v8`:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--v8=100
```

#### Issues & Fixes — Topic 2

- **Cosmos dependency calls missing from the transaction view.** The Cosmos .NET SDK (v3 ≥ 3.36.0 non-preview — `catalog-api` is on 3.46.1) ships with its own distributed tracing **off by default**. Being instrumented at the ASP.NET Core/OpenTelemetry level doesn't automatically mean every SDK's calls get traced — Cosmos needs its own separate opt-in. Two-part fix (already applied in `catalog-api-v8`): (1) an experimental `AppContext` switch, set as the very first line of `Program.cs` before the host builder — this is what makes Azure SDK clients emit Activities at all; (2) a matching option flipped on the Cosmos client itself, without which Cosmos calls stay invisible to tracing even with the switch above on.
- **`UseAzureMonitor()` only reads the connection string from the exact `APPLICATIONINSIGHTS_CONNECTION_STRING` env var** (or an `AzureMonitor:ConnectionString` config section) — never the more `IConfiguration`-idiomatic `ApplicationInsights:ConnectionString`. Putting the value under the wrong key compiles, runs, and produces zero telemetry with no error — worth knowing before troubleshooting "nothing showed up in the Portal" live.
- **Adding `Azure.Monitor.OpenTelemetry.AspNetCore` can silently break local `DefaultAzureCredential` fallback.** The package pulls in `Azure.Core` ≥ 1.60.0 transitively — a jump from the 1.53.0 that `Microsoft.Azure.Cosmos` alone resolves to — which changes how a failed Managed Identity probe is classified: instead of the `CredentialUnavailableException` that lets `DefaultAzureCredential` fall through to `az login`, it throws a hard `AuthenticationFailedException` that stops the chain dead, breaking local `dotnet run` with zero changes to any credential code. `catalog-api-v8` already carries the fix — `ExcludeManagedIdentityCredential` gated on whether `IDENTITY_ENDPOINT` is present (see `Program.cs`) — mentioned here so it's not a mystery if asked about live.

### 2.3 Live demo — instrumenting `catalog-ui`

Same package, same one-line wiring — already part of the existing `catalog-ui` project (grown in place, same as Topic 1's count pill — no new versioned folder), living there since the `catalog-ui:v1` image first deployed in Module 1. `UseAzureMonitor()` only activates when `APPLICATIONINSIGHTS_CONNECTION_STRING` is present (see `Program.cs`), so it's been silently skipped on every request until now. **No new `catalog-ui` build, push, or deploy is needed** — just wiring the connection string into the already-running container as an app setting and restarting to pick it up.

A single line near the top of `Program.cs` auto-instruments both incoming requests **and** the `HttpClient` `CatalogProxyController` already uses to call `catalog-api`, so that HttpClient call becomes a Dependency, correlated by Operation Id with whatever `catalog-api` does in response.

`$connString` was set back in §2.2 — if this is a new terminal session (e.g. picking back up after a break), re-fetch it first; it isn't in `variables.ps1` since it's a derived runtime value, not a fixed resource name:

```powershell
$connString = az monitor app-insights component show --app $APPI --resource-group $RG --query connectionString --output tsv
```

Sets the App Insights connection string and restarts to pick it up:

```powershell
az webapp config appsettings set --name $WEBAPP --resource-group $RG --settings "APPLICATIONINSIGHTS_CONNECTION_STRING=$connString"
```

```powershell
az webapp restart --name $WEBAPP --resource-group $RG
```

Browse `https://$WEBAPP.azurewebsites.net` and use the app for a moment (load the grid) — it should work exactly as before. In the Portal, open `appi-estiam-dev-2` → **Transaction Search** → a recent `GET api/titles/count` on `catalog-ui`. It should share the same Operation Id as the matching `GET /titles/count` on `catalog-api` — one correlated timeline spanning both apps and Cosmos DB underneath. Confirmed live: `cloud_RoleName` shows the real resource names (`webapp-estiam-dev-2`, `app-estiam-dev-2`) automatically once deployed, no manual configuration needed.

**Note for students watching the transaction list:** `GET /robots933456.txt` entries are App Service's own internal availability probe, not real traffic — the App Service equivalent of the platform-noise gotcha students may recall from Function Apps.

## 3. Topic 3 — Custom Events (`TrackEvent`) — mention only

Not built live this run — a single slide, time permitting.

`TelemetryClient.TrackEvent("TitleAdded", properties)` is the opt-in layer above automatic Requests/Dependencies/Exceptions: business-meaning counters you define explicitly ("how many titles were added today"), separate from debugging logs (Traces). Useful to know exists; not required for the project.

#### Issues & Fixes — Topic 3

- None — mention-only, not built live this run.
