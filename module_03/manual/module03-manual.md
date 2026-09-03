# Module 3 — Azure Security: Authentication & Secure Solutions

Modern Enterprise Software Engineering — Day 1, Afternoon.

This module continues directly in the shared course resource group from Modules 1–2 — no new resource group is created. If you're picking the course back up on a new day, re-run `materials/variables.ps1` before continuing. Confirm the Catalog app chain (`catalog-api-v4`/`catalog-ui`) is still up before starting — this module builds directly on it.

---

## 1. Topic 1 — Authentication vs. Authorization (concept)

### 1.1 Concept summary

Two different questions get conflated constantly, and Azure treats them as two entirely separate mechanisms.

|                | Authentication (AuthN) | Authorization (AuthZ)          |
| -------------- | ---------------------- | ------------------------------ |
| **Question**   | "Who are you?"         | "What are you allowed to do?"  |
| **Handled by** | Microsoft Entra ID     | RBAC role assignments          |
| **Result**     | A verified identity    | Allowed / denied, per resource |

An identity can pass AuthN and still fail AuthZ — that's not a bug, it's the whole point of splitting them. The class already saw this twice without the vocabulary for it: Module 1's App Service and Container Apps deploys both needed **explicit registry credentials** to pull the Catalog images from a private ACR — the registry didn't just trust "your own" resources by default (an AuthN/AuthZ setup problem, solved there with an admin username/password). Module 2's Blob Storage demo showed **Owner** (a control-plane role) failing to authorize a data-plane operation — you were a fully authenticated Owner, but blob upload was still denied until the right data-plane role was granted (AuthZ). Today's demo makes a failure like that deliberate and then fixes it live — and along the way, removes the registry-credential workaround from Module 1 entirely.

**Managed Identity** is Azure's answer to machine-to-machine AuthN: an identity Azure creates and manages for a specific resource, with no password that ever exists for a human to type, store, or leak.

| Type                | What it is                                                                              |
| ------------------- | --------------------------------------------------------------------------------------- |
| **System-assigned** | Tied to one resource's lifecycle — created and destroyed with it. Used in today's demo. |
| **User-assigned**   | A standalone identity created once and attached to several resources.                   |

That identity has two possible destinations:

- **Entra-ID-aware services** (Azure SQL, Storage, Cosmos DB, Service Bus, Key Vault, Container Registry) — authenticate directly. No secret exists anywhere.
- **Everything else** (third-party APIs, legacy connection-string-only systems) — use the identity to unlock Key Vault, retrieve a secret, then use it the traditional way. This is the fallback pattern.

Today's demo shows both: the Key Vault fallback first (2.2), then the direct pattern against two services already in the Catalog stack that turn out to be Entra-ID-aware — Cosmos DB (2.3) and the ACR itself (2.4).

**RBAC** governs AuthZ, and it has a hard split worth repeating from Module 2:

|                   | Control plane                       | Data plane                                                                                                |
| ----------------- | ----------------------------------- | --------------------------------------------------------------------------------------------------------- |
| **Manages**       | The resource itself (create/delete) | The data inside it (read/write)                                                                           |
| **Example roles** | Owner, Contributor                  | Storage Blob Data Contributor, Key Vault Secrets User/Officer, Cosmos DB Built-in Data Reader/Contributor |

Owner on a subscription or resource group never implies data-plane access. This was true for Blob Storage (Module 2) and is equally true for Key Vault and Cosmos DB's data-plane roles (this module — and note the Cosmos DB data-plane roles use their own separate command, `az cosmosdb sql role assignment create`, not the generic `az role assignment create`).

#### Issues & Fixes — Topic 1

- None — conceptual only, no live commands run this topic.

## 2. Topic 2 — Secure Solutions: Managed Identity (live demo)

Three parts, each built and verified before the next: Key Vault first (the classic fallback pattern), then two upgrades that remove secrets entirely from places the class has already seen them.

### 2.1 Concept summary — Azure Key Vault

A hardened, audited store for secrets, keys, and certificates — not a general-purpose database. Three things it holds:

| Type             | Examples                                                              |
| ---------------- | --------------------------------------------------------------------- |
| **Secrets**      | Connection strings, API keys, passwords — arbitrary strings           |
| **Keys**         | Cryptographic keys for encrypt/sign — material never leaves the vault |
| **Certificates** | TLS certs, with auto-renewal                                          |

Two setup details break the demo if missed:

- **Vaults default to the legacy "vault access policy" permission model, not Azure RBAC.** Role assignments are silently ignored unless the vault is created with `--enable-rbac-authorization true`.
- **Creating the vault (control-plane) does not let you write a secret into it (data-plane).** You need the **"Key Vault Secrets Officer"** role granted to yourself first — separate from **"Key Vault Secrets User"** (read-only), which is what gets granted to the Managed Identity later.

### 2.2 Live demo — Key Vault + Managed Identity

Resource group: the shared `$RG`. Region: `$LOCATION`. App: the Container App `$APP`, running `catalog-api-v4`. `catalog-api-v5` is already set up to read the Cosmos DB connection string from Key Vault via Managed Identity instead of from an app setting.

**Naming reminder:** `$KV` below is **globally unique** across Azure — if the create command fails because the name is taken, pick a different value and re-run.

Registers the Key Vault resource provider — took about 40 seconds to reach `Registered` when tested:

```powershell
az provider register --namespace Microsoft.KeyVault
```

Confirms registration finished — should print `Registered`, wait and re-check if it still says `Registering`:

```powershell
az provider show --namespace Microsoft.KeyVault --query registrationState --output tsv
```

Sets the vault name and creates it — **don't forget `--enable-rbac-authorization true`**, vault names are globally unique:

```powershell
$KV = "kv-estiam-dev-2"
```

```powershell
az keyvault create --name $KV --resource-group $RG --location $LOCATION --enable-rbac-authorization true
```

Fetches your own object id and the vault's resource id — both needed for the role assignment below:

```powershell
$MY_OBJECT_ID = az ad signed-in-user show --query id --output tsv
```

```powershell
$VAULT_ID = az keyvault show --name $KV --resource-group $RG --query id --output tsv
```

Grants yourself Key Vault Secrets Officer — without this, writing the secret below fails despite being Owner, the control-plane/data-plane split again:

```powershell
az role assignment create --role "Key Vault Secrets Officer" --assignee $MY_OBJECT_ID --scope $VAULT_ID
```

Fetches the real Cosmos DB connection string — using the real connection string makes the payoff concrete: the app securely reaches the database it already built:

```powershell
$COSMOS_CONN = az cosmosdb keys list --type connection-strings --name $COSMOS --resource-group $RG --query "connectionStrings[0].connectionString" --output tsv
```

Writes it into the vault as a secret:

```powershell
az keyvault secret set --vault-name $KV --name CosmosConnectionString --value $COSMOS_CONN
```

Enables a system-assigned Managed Identity on the Container App and captures its `principalId` — needed for the role assignment below and reused in 2.3/2.4:

```powershell
$APP_PRINCIPAL_ID = az containerapp identity assign --name $APP --resource-group $RG --system-assigned --query principalId --output tsv
```

Do this deliberately before granting the role below (skip ahead and come back): wires the Key Vault secret into the Container App, and watch it fail outright — `ERROR: ... Unable to get value using Managed identity system for secret cosmos-conn`. Unlike a Function App's `@Microsoft.KeyVault(...)` app setting (which resolves lazily and fails silently at runtime), Container Apps validates Key Vault access **at secret-creation time** — the whole command errors, confirmed live testing this step:

```powershell
az containerapp secret set --name $APP --resource-group $RG --secrets "cosmos-conn=keyvaultref:https://$KV.vault.azure.net/secrets/CosmosConnectionString,identityref:system"
```

This is the AuthZ fix — grants the Container App's identity read access to the vault. RBAC propagation took under a minute in testing:

```powershell
az role assignment create --role "Key Vault Secrets User" --assignee $APP_PRINCIPAL_ID --scope $VAULT_ID
```

Re-run the `az containerapp secret set` command above — it now succeeds. Build and push `catalog-api-v5` (run from `catalog-api-v5/`) — re-run `az acr login` first if the terminal session is new since Module 1/2, or if the push below fails with an authentication error:

```powershell
az acr login --name $ACR
```

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v5" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v5"
```

`BlobStorage__*` is unchanged from `v4` — `$STORAGE_CONN` from Module 2 doesn't survive closing the terminal, so re-fetch it if you're picking the course back up on a new day:

```powershell
$STORAGE_CONN = az storage account show-connection-string --name $STORAGE --resource-group $RG --query connectionString --output tsv
```

Deploys the new image — **`CosmosDb__ConnectionString` now points at `secretref:cosmos-conn`, not a literal value**, so Container Apps resolves it from the secret configured above:

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v5" --set-env-vars "CosmosDb__ConnectionString=secretref:cosmos-conn" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=$STORAGE_CONN" "BlobStorage__ContainerName=posters"
```

No `--revision-suffix` was passed above, so fetch the new revision as the most recently created one:

```powershell
$NEW_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "sort_by(@, &properties.createdTime)[-1].name" --output tsv
```

Moves traffic to the new revision:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

Open the deployed `catalog-ui` and confirm real Cosmos DB titles still load. Confirmed live: real data came back through the full chain — `catalog-ui` → `catalog-api-v5` → Key Vault (via Managed Identity) → Cosmos DB — with no connection string anywhere in the app's configuration.

### 2.3 Upgrade — Cosmos DB direct via Managed Identity

Key Vault just solved the "everything else" case from 1.1 — a legacy connection-string-only-shaped secret. But Cosmos DB isn't that kind of service: it's **Entra-ID-aware**, meaning the Managed Identity from 2.2 can authenticate to it directly, no secret anywhere, not even in Key Vault. This section builds that upgrade on the same identity already granted in 2.2.

Cosmos DB's data-plane roles use their **own command**, not the generic `az role assignment create` — a gotcha worth calling out explicitly, since the mistake (using the generic command against a Cosmos DB scope) fails in a way that doesn't obviously point at "wrong command". Lists the two built-in roles — **Cosmos DB Built-in Data Reader** and **Cosmos DB Built-in Data Contributor** — parallel to (but a completely separate system from) generic Azure RBAC roles:

```powershell
az cosmosdb sql role definition list --account-name $COSMOS --resource-group $RG --query "[].{Name:roleName, Id:id}" -o table
```

Fetches the Data Contributor role's id directly, rather than copying it from the table above:

```powershell
$COSMOS_ROLE_ID = az cosmosdb sql role definition list --account-name $COSMOS --resource-group $RG --query "[?roleName=='Cosmos DB Built-in Data Contributor'].id | [0]" --output tsv
```

`catalog-api-v6` is already set up to authenticate to Cosmos DB directly via `DefaultAzureCredential` — no connection string, no Key Vault.

Grants yourself the Data Contributor role — needed so the Container App's identity (and your own `az login` session, for anyone running it locally) can authenticate. `--scope "/"` means the whole account — a real deployment might scope this to one database/container instead:

```powershell
az cosmosdb sql role assignment create --account-name $COSMOS --resource-group $RG --role-definition-id $COSMOS_ROLE_ID --principal-id $MY_OBJECT_ID --scope "/"
```

Grants the same role to the Container App's managed identity from 2.2 — this is what lets the _deployed_ app skip Key Vault entirely:

```powershell
az cosmosdb sql role assignment create --account-name $COSMOS --resource-group $RG --role-definition-id $COSMOS_ROLE_ID --principal-id $APP_PRINCIPAL_ID --scope "/"
```

Build and push the image:

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v6" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v6"
```

Fetches the Cosmos DB account endpoint — the direct-auth path needs the URL, not a connection string:

```powershell
$COSMOS_ENDPOINT = az cosmosdb show --name $COSMOS --resource-group $RG --query documentEndpoint --output tsv
```

Deploys — the Key Vault secret reference (`cosmos-conn`) from 2.2 is no longer used by this revision; it's fine to leave it configured on the app for now, nothing references it:

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v6" --set-env-vars "CosmosDb__AccountEndpoint=$COSMOS_ENDPOINT" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=$STORAGE_CONN" "BlobStorage__ContainerName=posters" --remove-env-vars "CosmosDb__ConnectionString"
```

No `--revision-suffix` was passed above, so fetch the new revision as the most recently created one:

```powershell
$NEW_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "sort_by(@, &properties.createdTime)[-1].name" --output tsv
```

Moves traffic to the new revision:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

Open `catalog-ui` again and confirm real titles still load — same check as 2.2, this time with **zero secrets** involved in reaching Cosmos DB.

### 2.4 Upgrade — ACR pull via Managed Identity (closing a Module 1 gotcha)

One more Entra-ID-aware service was sitting in the stack the whole time, unnoticed: **Azure Container Registry itself**. Module 1 needed `az acr update --admin-enabled true` plus an explicit username/password on both the App Service and the Container App just to pull a private image — the very AuthN/AuthZ workaround called out back in 1.1. Verified live at the start of this module: `$WEBAPP` still had `DOCKER_REGISTRY_SERVER_USERNAME` set and no managed identity, and the ACR still had `adminUserEnabled: true` — exactly as Module 1 left it. This section removes that workaround from **both** places it appeared, using the same Managed Identity mechanism as 2.2/2.3.

**Container App**

Fetches the ACR's resource id — needed for both role assignments in this section:

```powershell
$ACR_ID = az acr show --name $ACR --resource-group $RG --query id --output tsv
```

Grants `AcrPull` to the Container App's identity from 2.2/2.3 — one identity, three different destinations across this module:

```powershell
$APP_PRINCIPAL_ID = az containerapp identity assign --name $APP --resource-group $RG --system-assigned --query principalId --output tsv
```

```powershell
az role assignment create --role "AcrPull" --assignee $APP_PRINCIPAL_ID --scope $ACR_ID
```

Switches the registry auth to that identity — the registry entry now shows `"identity": "system"` with empty username/password. This command group is in preview (`az containerapp registry` — noted by the CLI itself):

```powershell
az containerapp registry set --name $APP --resource-group $RG --server "${ACR}.azurecr.io" --identity system
```

Do this deliberately — swapping the registry config alone doesn't re-pull an already-running image. Forcing a new revision (same image, new suffix) is what actually exercises the identity-based pull path. Confirmed live: the revision came up `Running` and served real data through its own FQDN, before any traffic was moved to it:

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v6" --revision-suffix acrmi
```

**App Service**

Enables a system-assigned Managed Identity on the Web App and captures its `principalId` — a separate identity from the Container App's, each resource gets its own:

```powershell
$WEBAPP_PRINCIPAL_ID = az webapp identity assign --name $WEBAPP --resource-group $RG --query principalId --output tsv
```

Grants it `AcrPull`:

```powershell
az role assignment create --role "AcrPull" --assignee $WEBAPP_PRINCIPAL_ID --scope $ACR_ID
```

Switches the App Service's registry auth to that identity — dedicated flags exist for this (`--acr-use-identity`, `--acr-identity`), no need for the generic `--generic-configurations` JSON escape-hatch:

```powershell
az webapp config set --resource-group $RG --name $WEBAPP --acr-use-identity true --acr-identity "[system]"
```

Removes the now-dead-weight admin-credential app settings — the identity-based config path doesn't read them:

```powershell
az webapp config appsettings delete --name $WEBAPP --resource-group $RG --setting-names DOCKER_REGISTRY_SERVER_URL DOCKER_REGISTRY_SERVER_USERNAME DOCKER_REGISTRY_SERVER_PASSWORD
```

**Proving it, not just configuring it**

Configuring identity-based pull doesn't by itself prove the admin credentials are no longer needed — the old ones could still be silently doing the work. Disables the ACR admin user entirely:

```powershell
az acr update --name $ACR --admin-enabled false
```

Restarts the Web App to force a fresh pull:

```powershell
az webapp restart --name $WEBAPP --resource-group $RG
```

Open `catalog-ui` in the browser and confirm it still loads real data — with the ACR admin user fully disabled, both `catalog-ui` (App Service) and `catalog-api-v6` (Container App, via the `acrmi` revision) still served real data end to end. Neither app has depended on a registry password since — the gotcha from Module 1 is closed for the rest of the course.

#### Issues & Fixes — Topic 2

- **Vaults default to the legacy "vault access policy" permission model, not Azure RBAC.** Role assignments are silently ignored unless the vault is created with `--enable-rbac-authorization true`.
- **Creating the vault (control-plane) does not let you write a secret into it (data-plane).** The **"Key Vault Secrets Officer"** role has to be granted to yourself first — separate from **"Key Vault Secrets User"** (read-only), which is what gets granted to the Managed Identity later.
- **`az containerapp secret set` fails outright if the identity's Key Vault role isn't granted yet** — `ERROR: ... Unable to get value using Managed identity system for secret cosmos-conn`. Unlike a Function App's `@Microsoft.KeyVault(...)` app setting (which resolves lazily and fails silently at runtime), Container Apps validates Key Vault access **at secret-creation time** — the whole command errors. Re-running the same command after granting the role succeeds.
- **Cosmos DB's data-plane roles use their own command**, `az cosmosdb sql role assignment create`, not the generic `az role assignment create` — using the generic command against a Cosmos DB scope fails in a way that doesn't obviously point at "wrong command."
- **`az containerapp registry set` is a preview command group** — noted by the CLI itself when used to switch the registry auth to Managed Identity.

## 3. Topic 3 — End-user authentication (concept overview)

Not live-tested this module — covered conceptually. Managed Identity only solves machine-to-machine trust; it says nothing about a human proving who they are at a browser.

- **App Registration** — represents the application itself in Entra ID, so users can sign into it (distinct from a Managed Identity, which represents one Azure resource).
- **OAuth2 / OpenID Connect** — the protocol: sign in once, receive a temporary token (a JWT), attach it as a `Bearer` header on every subsequent request instead of re-entering credentials.
- **In .NET**, `Microsoft.Identity.Web` + the `[Authorize]` attribute handle almost all of this via configuration, not hand-rolled code.
- **Simplest possible case**: App Service's built-in **Authentication ("Easy Auth")** toggle requires no code at all — it forces the Microsoft sign-in redirect just by being enabled on the App Service.

#### Issues & Fixes — Topic 3

- None — conceptual only, no live demo in this module.
