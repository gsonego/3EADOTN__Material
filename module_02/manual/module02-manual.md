# Module 2 — Develop Azure Storage Solutions

Modern Enterprise Software Engineering — Day 1, Afternoon (2h).

This module continues directly in the shared course resource group from Module 1 — no new resource group is created. If you're picking the course back up on a new day, re-run `materials/variables.ps1` (or re-declare `$RG`, `$ACR`, `$ENVNAME`, `$APP`, `$WEBAPP` individually) before continuing; these variables don't survive closing the terminal.

---

## 1. Topic 1 — Azure Cosmos DB

### 1.1 Concept summary

Cosmos DB is Azure's globally distributed, multi-model NoSQL database. This course uses the SQL (Core) API — the native, default API that pairs directly with the .NET SDK. Four ideas matter:

| Level         | What it is                                                                | Demo mapping                                                                                  |
| ------------- | ------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| **Account**   | The top-level resource — the whole database service instance.             | `cosmos-estiam-dev-2`                                                                         |
| **Database**  | A namespace inside the account. No pricing/scaling of its own by default. | `CatalogDb`                                                                                   |
| **Container** | Where data actually lives — and where the partition key is defined.       | `Titles` (partition key: `/genre`)                                                            |
| **Item**      | One JSON document. Schema-free — fields can vary between items.           | `{ "id", "title", "genre", "year", "description", "posterUrl" }` — the Catalog's title record |

**Partition key:** spreads items across physical storage so reads/writes can run in parallel. A good key has many distinct values that match your dominant query pattern (e.g. `customerId`, `category`). A key with very few distinct values — or one dominant value — creates a "hot partition": most traffic lands on one physical partition and throttles, even while others sit idle. This is a design decision made once; changing it later means migrating data into a new container.

**Honest trade-off, not a best practice:** `/genre` is used here because it matches the Catalog UI's existing genre-filter chips, which makes the demo intuitive — but with only ~6 genres, it's a low-cardinality key. A production catalog with millions of titles would likely partition by `id` (or something with far more distinct values) and let genre filtering happen as a query, not a partition boundary. Good discussion point: ask what a real streaming service's title catalog would key on instead.

**RU/s (Request Units per second):** every operation costs RUs — a point read might cost ~1, a write more, a cross-partition query more still. RU/s is your throughput budget per second, either provisioned up front, autoscaled, or fully serverless (pay-per-request). Exceeding it throttles requests (HTTP 429) rather than failing outright — the SDK retries automatically by default.

| Consistency level                   | Guarantee                                                | Trade-off                                                          |
| ----------------------------------- | -------------------------------------------------------- | ------------------------------------------------------------------ |
| **Strong**                          | Reads always see the latest committed write, everywhere. | Highest latency, lowest availability during regional failover.     |
| **Bounded Staleness**               | Reads lag writes by a fixed time or version window.      | Predictable staleness, still fairly strict.                        |
| **Session** (default, used in demo) | A single client always sees its own writes.              | Best real-world balance — used unless you need stricter or looser. |
| **Consistent Prefix**               | Reads never see writes out of order.                     | Order guaranteed; some staleness allowed.                          |
| **Eventual**                        | All replicas converge — eventually.                      | Fastest, cheapest — fine for non-critical data like like-counts.   |

### 1.2 Live demo — CLI: create the account, database & container

Resource group: the shared `$RG` (`rg-estiam-dev-2`) from Module 1. Region: `$LOCATION` (`westcentralus`), same as the rest of the environment.

**Naming convention reminder (from Module 1):** the first time this module names a new resource, store it in a PowerShell variable and reuse the variable in every command after that. Define each variable right before its first use. `$COSMOS` below is **globally unique** across Azure — if the create command fails because the name is taken, pick a different value and re-run. These variables only last for the current terminal session — if you're starting a new day, re-run `materials/variables.ps1` first.

Registers the Cosmos DB resource provider — must run before creating the account:

```powershell
az provider register --namespace Microsoft.DocumentDB
```

Confirms registration finished — should print `Registered` (took ~70 seconds to reach that state when tested; wait and re-check if it still says `Registering`):

```powershell
az provider show --namespace Microsoft.DocumentDB --query registrationState --output tsv
```

Sets the Cosmos DB account name and creates the account — this took a few minutes when tested, slower than most Module 1 resources. A region capacity error means retry with a different region; an already-taken name means append initials/digits (Cosmos DB account names are globally unique):

```powershell
$COSMOS = "cosmos-estiam-dev-2"
```

```powershell
az cosmosdb create --name $COSMOS --resource-group $RG --locations regionName=$LOCATION --default-consistency-level Session --kind GlobalDocumentDB
```

Creates the database — a namespace inside the account:

```powershell
az cosmosdb sql database create --account-name $COSMOS --resource-group $RG --name CatalogDb
```

Creates the container, with the partition key path set to `/genre` and the minimum manually-provisioned throughput (400 RU/s is enough for a classroom demo):

```powershell
az cosmosdb sql container create --account-name $COSMOS --resource-group $RG --database-name CatalogDb --name Titles --partition-key-path "/genre" --throughput 400
```

Prints the primary connection string — it carries a master key with full read/write access to the whole account. Never commit it to source control; it goes into `appsettings.json`/`appsettings.Development.json`, both of which should be gitignored (Module 3's Managed Identity removes the need for this secret entirely):

```powershell
az cosmosdb keys list --type connection-strings --name $COSMOS --resource-group $RG --query "connectionStrings[0].connectionString" --output tsv
```

### 1.3 Live demo — real persistence in the Catalog API

`catalog-api-v3` is already set up with real Cosmos DB persistence behind `GET/POST/PUT/DELETE /titles` — `catalog-api-v2` stays exactly as Module 1 left it, live and untouched. Worth knowing when you demo it: the route for `PUT`/`DELETE` only carries the title's `id`, not its `genre` — since `genre` is the partition key, and Cosmos doesn't support changing a partition key in place, changing a title's genre in the UI is a delete on the old partition followed by a create on the new one, same `id`. A good moment to connect back to 1.1's partition-key discussion — this is the concrete cost of that design decision.

Build, push, and deploy `catalog-api-v3` on its own — Blob Storage (Section 2.3) is a **separate release** (`catalog-api-v4`), deployed and verified only after this one is confirmed working, not bundled into the same deploy:

```powershell
 az acr login --name $ACR
```

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v3" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v3"
```

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v3" --set-env-vars "CosmosDb__ConnectionString=<connection string from 1.2>" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles"
```

Lists the revisions, so you have the new one's exact name for the next step:

```powershell
az containerapp revision list --name $APP --resource-group $RG --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight, Image:properties.template.containers[0].image}" --output table
```

Moves traffic to the new revision:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight <new-revision-name>=100
```

Open the deployed `catalog-ui` (Section 4, Module 1) and confirm its poster grid populates from real Cosmos DB data for the first time — add a title through the UI, refresh, it's still there.

#### Issues & Fixes — Topic 1

- None — no issues hit live for this topic.

## 2. Topic 2 — Azure Blob Storage

### 2.1 Concept summary

Blob Storage is Azure's object store for files of any type — photos, PDFs, videos, backups. Simpler hierarchy than Cosmos DB: no partition key concept, containers are just namespaces.

| Level               | What it is                                                   | Demo mapping                                                                                                                                                                                         |
| ------------------- | ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Storage account** | The top-level resource — the whole storage service instance. | `stestiamdev2`                                                                                                                                                                                       |
| **Container**       | A namespace inside the account. No further nesting.          | `posters` — the same container used for both this mechanics demo and the Catalog app's real poster uploads (Section 2.3), deliberately, so the demo exercises the real thing rather than a throwaway |
| **Blob**            | One file, any type or size.                                  | `hello.txt` for this demo (any file works to show the mechanics); a title's poster image once the app is wired up                                                                                    |

Blobs are private by default — nobody outside your account can open one directly, even with the exact URL.

| Tier        | Best for                                            | Storage / access cost                                      |
| ----------- | --------------------------------------------------- | ---------------------------------------------------------- |
| **Hot**     | Files touched constantly (this week's uploads).     | Highest storage cost, cheapest & instant access.           |
| **Cool**    | Rarely touched, might be needed soon (30+ days).    | Lower storage cost, costs more per access, still instant.  |
| **Archive** | Long-term backups, compliance, rarely-if-ever read. | Lowest storage cost by far — but retrieval can take hours. |

_Conceptual only this module — no live demo of tier changes (`az storage blob set-tier` if a student asks). Netflix analogy: new releases sit on Hot storage, the back-catalog sits in cheaper Cool/Archive storage — same content, different access pattern._

### 2.2 Live demo — SAS tokens: create, upload, private-by-default, generate access

Resource group: the shared `$RG` (`rg-estiam-dev-2`). Region: `$LOCATION` (`westcentralus`), same as the rest of the environment.

Sets the storage account name and creates it — `$STORAGE` is **globally unique** across Azure (3–24 chars, lowercase letters/numbers only), pick a different value if this is rejected. `Microsoft.Storage` should already be registered from Module 1 — if not, register it first with `az provider register --namespace Microsoft.Storage`:

```powershell
$STORAGE = "stestiamdev2"
```

```powershell
az storage account create --name $STORAGE --resource-group $RG --location $LOCATION --sku Standard_LRS
```

Creates the `posters` container — the same container used for both this mechanics demo and the Catalog app's real poster uploads (Section 2.3), deliberately, so the demo exercises the real thing rather than a throwaway. This step succeeded even _before_ the role granted below was assigned — container create doesn't need the data-plane role, only blob upload does:

```powershell
az storage container create --name posters --account-name $STORAGE --auth-mode login
```

Uploads a test file (run from `materials/module_02/`, where `hello.txt` lives) — this fails the first time with a permissions error:

```powershell
az storage blob upload --account-name $STORAGE --container-name posters --name hello.txt --file hello.txt --auth-mode login
```

`--auth-mode login` uses Azure AD authorization, which is SEPARATE from the account-level Owner role. Even as subscription Owner, the upload above was denied until the **"Storage Blob Data Contributor"** role was explicitly assigned. Azure splits control-plane permissions (Owner/Contributor — manage the resource itself) from data-plane permissions (Storage Blob Data Contributor/Reader — touch the data inside it), and not every container/blob operation draws that line in the same place — a natural preview of Module 3 (Security/RBAC). Gets the storage account's resource id, needed for the role assignment:

```powershell
az ad signed-in-user show --query id -o tsv
```

```powershell
az storage account show --name $STORAGE --resource-group $RG --query id --output tsv
```

Grants yourself the role — RBAC took under a minute to take effect when tested; re-running the upload command above after this succeeds:

```powershell
az role assignment create --role "Storage Blob Data Contributor" --assignee <your-user-or-object-id> --scope <storage-account-resource-id>
```

Prints the blob's plain URL:

```powershell
az storage blob url --account-name $STORAGE --container-name posters --name hello.txt --output tsv
```

Paste it into a browser — confirmed HTTP 409 `PublicAccessNotPermitted`. Good visual before the SAS token below. Generates a read-only SAS token (query-string portion only) — compute `--expiry` relative to the actual demo time (e.g. 1 hour ahead), don't hardcode a fixed timestamp:

```powershell
az storage blob generate-sas --account-name $STORAGE --container-name posters --name hello.txt --permissions r --expiry <UTC datetime> --https-only --auth-mode login --as-user --output tsv
```

Append the token to the plain URL from above as `<blob-url>?<sas-token>` and open it in a browser — now it works. If the very first try 403s, see the clock-skew note below.

#### Issues & Fixes — Topic 2

- **User-delegation SAS clock skew:** the very first request against a freshly generated `--as-user` SAS can 403 with `AuthenticationFailed` / "Signature not valid in the specified key time frame" — the delegation key's start time (`skt`) can land a second or so after the token was generated, and the request can arrive before that instant. Waiting a few seconds and retrying the _same_ URL (no need to regenerate) resolves it. Worth showing live — it looks alarming the first time.

### 2.3 Live demo — poster upload in the Catalog API

This is a **separate release from Section 1.3** — `catalog-api-v3` (Cosmos DB) ships and gets verified in class on its own first; Blob Storage is a distinct, separately-tested capability, not bundled into the same deploy. `catalog-api-v4` is already set up with poster upload: `POST /titles/{id}/poster` stores the uploaded file in the `posters` container and keeps only the blob name on the Cosmos item — never a URL, since URLs expire. `GET /titles` (and the create/update responses) generate a fresh, short-lived read-only SAS URL for `posterUrl` on every response — Section 2.2's "private by default, time-limited access" lesson, now happening in application code instead of the CLI.

Build, push, deploy as `:v4`:

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v4" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v4"
```

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v4" --set-env-vars "CosmosDb__ConnectionString=<...>" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=<storage connection string>" "BlobStorage__ContainerName=posters"
```

Moves traffic to the new revision:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight <new-revision-name>=100
```

### 2.4 End-to-end verification

Per the module's definition of done, the whole chain gets exercised together, not just each piece in isolation. Open the deployed `catalog-ui`, point its endpoint pill at the `catalog-api` Container App's FQDN, and create a title with a poster image through the UI — confirm it appears in the grid with the poster rendered, then refresh and confirm it's still there. This exercises Cosmos DB and Blob Storage together, the same way a student's browser would.
