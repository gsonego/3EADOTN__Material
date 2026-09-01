# Module 5 — Connect & Consume

Modern Enterprise Software Engineering — Day 2, Morning.

This module continues directly in the shared course resource group from Modules 1–4 — no new resource group is created. If you're picking the course back up on a new day, re-run `materials/variables.ps1` before continuing. Confirm the Catalog app chain (`catalog-api-v8`/`catalog-ui`) is still up before starting — this module builds directly on it.

---

## 1. Topic 1 — API Management

### 1.1 Concept summary

Modules 1–4 built one app that talks *outward* to Azure services — Cosmos, Blob, Key Vault. Module 5 flips the direction: now something talks *inward* to that app. The moment an API is exposed to any consumer you don't fully control, you inherit a set of problems that have nothing to do with your business logic — who's allowed to call this, how many times, what happens when the contract changes. Solving those inside `catalog-api` itself means every consumer-facing concern becomes application code. API Management's pitch is that you solve them once, in a layer that sits in front of any number of backends, while the backend itself stays completely unaware the gateway exists.

That last point is worth demonstrating, not just asserting: `catalog-api` has zero code awareness of APIM. It doesn't know it's behind a gateway. The gateway is bolted on from outside, and everything in this topic proves that.

Three ideas carry the whole topic:

1. **Import, don't hand-write.** APIM can synthesize its entire API surface — routes, methods, parameter shapes — from an OpenAPI document. `catalog-api` serves `/swagger/v1/swagger.json` live, so APIM reads that once and the API definition exists with zero manual entry.
2. **The subscription key is the product, not a footnote.** Once imported, APIM demands `Ocp-Apim-Subscription-Key` on every call. No key, no data — the whole "gateway as bouncer" idea in two requests.
3. **Policies are where the gateway earns its keep.** XML that runs on the request or response pipeline, before the backend ever sees the call or after it has already answered. This is logic that used to live in application middleware, now living in infrastructure config a platform team can own independently of the team shipping the API.

### 1.2 Live demo — provisioning API Management

Register the resource provider (only needs running once per subscription, but worth confirming on a subscription that hasn't used APIM before):

```powershell
az provider register --namespace Microsoft.ApiManagement
```

Check the registration state — it can take a minute to flip to `Registered`, though the create command below generally queues fine even mid-registration:

```powershell
az provider show --namespace Microsoft.ApiManagement --query "registrationState" -o tsv
```

Sets the APIM instance name and the publisher email it's registered under — `$APIM` is **globally unique** across Azure (it becomes part of the `azure-api.net` gateway URL), pick a different value here if the create command below rejects it:

```powershell
$APIM = "apim-estiam-dev-2"
```

```powershell
$EMAIL = "gsonego1@outlook.com"
```

Fire the create. This is the one command in the whole topic that takes real wall-clock time — a few minutes even on the Consumption tier — so kick it off and let it run in the background while the next section (Events vs Messages) is taught:

```powershell
az apim create --name $APIM --resource-group $RG --publisher-name "Estiam" --publisher-email "$EMAIL" --sku-name Consumption --location $LOCATION
```

Once it's provisioned, pull the gateway URL:

```powershell
az apim show --name $APIM --resource-group $RG --query "{gatewayUrl:gatewayUrl, publicIPAddresses:publicIpAddresses}" -o json
```

`publicIPAddresses` comes back `null` — that's not a mistake, and it matters later (see §1.7 below).

### 1.3 Live demo — importing the Catalog API

Get the Container App's public FQDN — this is what APIM needs to reach the real backend:

```powershell
$FQDN = az containerapp show --name $APP --resource-group $RG --query "properties.configuration.ingress.fqdn" -o tsv
```

Import the API definition straight from `catalog-api`'s live Swagger endpoint. **`--service-url` is required** — `catalog-api`'s OpenAPI document carries no `servers` entry, so without this flag APIM has an operation definition but no idea which backend to actually call:

```powershell
az apim api import --resource-group $RG --service-name $APIM --path catalog --api-id catalog-api --specification-format OpenApi --specification-url "https://$FQDN/swagger/v1/swagger.json" --service-url "https://$FQDN"
```

If the backend URL ever changes later (a redeploy, a new FQDN) without needing to re-import the whole API:

```powershell
az apim api update --resource-group $RG --service-name $APIM --api-id catalog-api --service-url "https://$FQDN"
```

### 1.4 Live demo — proving the gateway is the only sanctioned path in

Call the API through the gateway with **no key** — this should bounce with a `401`:

```powershell
Invoke-WebRequest -Uri "https://$APIM.azure-api.net/catalog/titles" -Method GET
```

Pull a subscription key. There's no native `az apim subscription` command (see Issues & Fixes), so this goes through `az rest` against the ARM API directly:

```powershell
$SUBID = az account show --query id -o tsv
```

```powershell
az rest --uri "/subscriptions/$SUBID/resourceGroups/$RG/providers/Microsoft.ApiManagement/service/$APIM/subscriptions?api-version=2022-08-01" -o json
```

The built-in all-access subscription's `name` is `master`. Retrieve its key:

```powershell
$KEY = az rest --method post --uri "/subscriptions/$SUBID/resourceGroups/$RG/providers/Microsoft.ApiManagement/service/$APIM/subscriptions/master/listSecrets?api-version=2022-08-01" --query primaryKey -o tsv
```

Call again, this time **with** the key — should return `200`:

```powershell
Invoke-WebRequest -Uri "https://$APIM.azure-api.net/catalog/titles" -Headers @{ "Ocp-Apim-Subscription-Key" = $KEY }
```

### 1.5 Live demo — repointing the Catalog UI at the gateway

`catalog-ui`'s `CatalogProxyController` has sent the `Ocp-Apim-Subscription-Key` header on every outbound call since Module 1, and its base URL + key have always been runtime settings stored client-side (Settings modal → localStorage), not code. Moving the whole app behind APIM is a browser settings change, not a redeploy:

1. Open the deployed `catalog-ui` app.
2. Click the endpoint pill to open **Settings**.
3. Set **Base URL** to `https://$APIM.azure-api.net/catalog` and **API Key** to `$KEY`.
4. Save.

The grid reloads with real data — same running app, same code, now going through APIM. Clearing the key and re-saving confirms the app now genuinely needs one to function.

### 1.6 Live demo — adding policies

There's no native `az apim api policy` CLI command — this is done in the Portal, which is also simpler: the policy code editor pre-populates the full skeleton, so only two lines get added, not a whole document.

1. Azure Portal → your APIM instance (`$APIM`) → **APIs** → **catalog-api** → **Design** tab.
2. Under **All operations**, in the **Inbound processing** panel, click the **`</>`** (code editor) icon. The skeleton:
   ```xml
   <policies>
       <inbound>
           <base />
       </inbound>
       <backend>
           <base />
       </backend>
       <outbound>
           <base />
       </outbound>
       <on-error>
           <base />
       </on-error>
   </policies>
   ```
3. In `<inbound>`, add a rate limit **before** `<base />`. This throttles per subscription automatically — no custom key needed:
   ```xml
   <rate-limit calls="3" renewal-period="30" />
   ```
4. In `<outbound>`, add a header **after** `<base />`. This runs after `catalog-api` has already answered, before the response reaches the caller — proof the gateway can rewrite responses, not just gate requests. `exists-action="override"` forces the value regardless of whether the backend happens to send one (it doesn't, here):
   ```xml
   <set-header name="X-Gateway" exists-action="override">
       <value>apim-estiam-dev-2</value>
   </set-header>
   ```
5. **Save.**

Prove the rate limit is real — four calls inside the 30-second window, the last one should come back `429`:

```powershell
1..4 | ForEach-Object {
    $r = Invoke-WebRequest -Uri "https://$APIM.azure-api.net/catalog/titles" -Headers @{ "Ocp-Apim-Subscription-Key" = $KEY } -SkipHttpErrorCheck
    "$_`: $($r.StatusCode)"
}
```

Open DevTools → Network → the `/titles` response → Headers to see `X-Gateway: apim-estiam-dev-2` on a response `catalog-api`'s own code never wrote.

### 1.7 Tiers, and what "locking the back door" actually costs

APIM being "the front door" only matters if the old path is actually closed. The Container App's own FQDN still resolves directly right now — gateway or no gateway. What does it actually take to close it? The honest answer is sharper than a scripted one:

- **IP allow-list on the Container App's ingress** — needs a static outbound IP from APIM. Consumption tier doesn't have one (`publicIPAddresses: null`, confirmed in 1.2) — nor do any of the v2 tiers (Basic v2/Standard v2/Premium v2). Only the classic tiers (Developer/Basic/Standard/Premium) get a static IP, and those take 30–45 minutes to provision.
- **Client certificate (mTLS)** — Container Apps' `--client-certificate-mode require` only requires *a* certificate be presented; the platform doesn't validate it. Real rejection of untrusted certs needs app code reading `X-Forwarded-Client-Cert` — meaning `catalog-api` itself would have to change.
- **VNet injection** — the real production answer. Container Apps environment on internal-only ingress inside a VNet, APIM in the same VNet, and the FQDN simply isn't publicly resolvable. Requires Developer or Premium tier.

There is no tier that is both fast to provision and gives real network control for free. Provisioning time isn't just about waiting — on Consumption, it's the whole reason you don't get a static IP.

#### Issues & Fixes — Topic 1

- **`az apim api import` succeeds but every call 500s:** `--service-url` was omitted. `catalog-api`'s Swagger doc has no `servers` entry, so APIM has no backend target without it. Fix with `az apim api update --service-url` (no re-import needed).
- **No native `az apim subscription` command:** `az apim subscription list` fails with `"'subscription' is misspelled or not recognized by the system."` — there is no such command group at all. Use `az rest` against `/subscriptions/.../service/$APIM/subscriptions?api-version=2022-08-01` instead.
- **`<rate-limit-by-key>` policy rejected on the Consumption tier:** `"Policy is not allowed in 'Consumption' sku."` — a hard rejection, not a soft limit. `rate-limit-by-key` applies to Developer/Basic/Standard/Premium (+v2) only. Use plain `<rate-limit calls="3" renewal-period="30" />` instead — it applies to *all* tiers, including Consumption, and is scoped per-subscription automatically.

---

## 2. Topic 2 — Events vs Messages

### 2.1 Concept summary

Everything in this topic reduces to one question: **who's aimed at whom?**

An **event** is an announcement — "this happened." The publisher (Blob Storage, say) doesn't know or care who's listening, and zero consumers is a perfectly valid state; the event just evaporates. This is Event Grid and Event Hubs territory.

A **message** is an instruction — "do this." It's addressed to a specific consumer, and it waits for that consumer, sitting in a queue until someone picks it up. If nobody's listening, the message accumulates instead of disappearing. This is Service Bus and Storage Queues territory.

Four terms follow directly from that split, and each one is exam-gold:

- **Competing consumers.** Scaled-out instances of a consumer share one queue — each message goes to exactly one instance, not all of them. Contrast with an event subscription, where every subscriber gets its own copy (fan-out).
- **At-least-once delivery → idempotency.** Both Service Bus and Storage Queues guarantee a message is delivered at least once, not exactly once. A consumer has to be safe to process the same message twice.
- **Poison / dead-letter.** A message that keeps failing gets moved out of the way after N attempts, so it can't jam the whole pipeline.
- **Peek-lock vs receive-and-delete.** Peek-lock hides a message from other consumers without deleting it until the consumer explicitly completes it — crash mid-processing and it becomes visible again. Receive-and-delete removes it immediately on read, trading safety for less overhead. Peek-lock is what makes at-least-once delivery actually safe.

**The decision, as one question:** does this need to reach one specific place, and wait if that place is busy? Yes → message → queue. No → event → announce and move on.

Practical mapping: **Event Grid** for low-volume, reactive plumbing ("a blob was created"); **Event Hubs** for high-volume streaming (telemetry, logs); **Service Bus** for queueing with real features — sessions (ordered per-customer processing), topics (one message, multiple independent subscriptions), transactions (all-or-nothing across operations); **Storage Queues** for a plain, cheap queue — no sessions, no topics, but free with any storage account.

Nothing in this topic is built live — Service Bus stays a concept, covered on slides only. Topic 3's build puts these ideas to work: an event (Blob → Event Grid) triggers work that then hands off through a message (Storage Queue) to a Function, chaining both patterns rather than picking one.

#### Issues & Fixes — Topic 2

- None — no live resources this topic.

---

## 3. Topic 3 — The Build: Poster Normalization

### 3.1 Concept summary

**Functions 101 (a syllabus gap this module closes).** Everything up to now has been "always-on" compute — a Container App running whether or not anyone's calling it. A Function is the opposite: it doesn't exist as a running process until something triggers it, and it stops billing the moment it's done. The trigger is the whole model — a Function isn't "an app with a `Main`," it's a method decorated with *what wakes it up* (`[QueueTrigger]`, `[HttpTrigger]`, `[BlobTrigger]`, `[EventGridTrigger]`, ...) and *what it needs handed to it when it does* (bindings — input/output shortcuts so you don't hand-write the SDK calls to read a queue message or write a blob). That trigger/binding pair is the named AZ-204 skill, and this build uses `QueueTrigger`. We use the Consumption plan (true pay-per-execution, scales to zero) and the isolated worker model (.NET 8 running as its own process rather than in-process with the Functions host — the modern default).

**The wiring:**

```
poster uploaded to Blob Storage (posters/ container)
        |
        v  Microsoft.Storage.BlobCreated event
   Event Grid subscription (filtered to posters/, delivers straight to a queue)
        |
        v
   Storage Queue: poster-jobs
        |
        v  QueueTrigger
   Function: poster-normalizer / NormalizePoster
        |
        v  resizes to 2:3 with ImageSharp, overwrites the SAME blob
   done -- same SAS URL now serves the normalized image
```

The point this whole module keeps coming back to: **`catalog-api` is never touched or redeployed.** A whole new capability ships into a running system by attaching to events it already emits, not by opening its code. The explicit-producer alternative — `catalog-api` calling `QueueClient.SendMessageAsync` itself right after an upload — is maybe six lines of code, but it means every future producer of "a poster changed" has to remember to write to that queue. The event-driven version doesn't care who or what created the blob.

**Two things that matter before you hit them live:**

- **Idempotency is mandatory, not optional.** The Function overwrites the same blob it was triggered by. Overwriting a blob fires *another* `BlobCreated` event. Without a guard, this loops forever. Fix: the Function sets blob metadata `normalized=true` after processing, and returns immediately if that metadata is already there — checked *before* any image work happens, not after.
- **Why a queue sits between Event Grid and the Function at all**, rather than an `EventGridTrigger` Function directly — this is the events-vs-messages distinction from Topic 2 made concrete. Event Grid's own retry is short-lived and push-based; a Storage Queue gives buffering, a real retry/visibility-timeout story, and a poison queue (`poster-jobs-poison`, auto-created after 5 failed dequeues) if a malformed image keeps crashing the Function. Competing consumers, at-least-once, poison — the same vocabulary from Topic 2, now doing real work.

**A related bug, mentioned but not built:** deleting a title from the catalog doesn't delete its poster — nothing tells Blob Storage a delete happened. `Microsoft.Storage.BlobDeleted` exists as a system event too, but it only fires *after* a blob is deleted — it can't be the thing that decides to delete the orphaned poster in the first place, because nothing currently deletes it. The platform gives you `BlobCreated` for free because Blob Storage already knows when *it* changed something; it can't give you "a Cosmos item was deleted" for free, because that decision lives entirely in `catalog-api`'s own code. The realistic fix is two lines in the DELETE handler (`BlobClient.DeleteAsync()`) — not an eventing problem at all. Good System Topic vs. "the platform can't read your mind" contrast, not built live.

### 3.2 Live demo — provisioning the queue and the Function App

Reusing `$STORAGE` (`stestiamdev2`) from Module 2 for the queue — no new storage account needed:

```powershell
az storage queue create --name poster-jobs --account-name $STORAGE --auth-mode login
```

New variable, first use:

```powershell
$FUNC = "func-estiam-dev-2"    # must be globally unique across Azure
```

Provision the Function App — Consumption plan, Linux, .NET 8 isolated, reusing `$STORAGE` as the Function App's own storage account (this is what gives zero-config binding to `poster-jobs` later — `AzureWebJobsStorage` already points at `stestiamdev2`):

```powershell
az functionapp create --name $FUNC --resource-group $RG --storage-account $STORAGE --consumption-plan-location $LOCATION --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --os-type Linux
```

**This is the version of the command that looks right and provisions cleanly, but doesn't actually work in `westcentralus` on this subscription right now — see 3.5 and Issues & Fixes.** The version that's actually used ends up specifying a different region for the Function App's compute; keep reading before running this live in class.

### 3.3 The Function code — already written, not built live

Per the "minimum code exposure" call for this topic: `poster-normalizer` ships ready-made in `materials/module_05/poster-normalizer/`, right alongside `catalog-api`/`catalog-ui` in this course's own materials -- nothing here gets scaffolded or typed live in front of the class. What's below is enough to explain what it does, not to reproduce it.

`NormalizePoster` does five things, in order: decode the incoming message (Event Grid base64-encodes it -- detect and decode rather than assume, validated live, see 3.5), parse the event and pull the blob name out of its `subject` field, check the idempotency guard *before* touching the image, resize with ImageSharp and overwrite the blob, then set `normalized=true` metadata so the guard catches the overwrite's own follow-on event.

Two lines carry the whole AZ-204 point here -- the trigger/binding pair:

```csharp
[Function("NormalizePoster")]
public async Task Run([QueueTrigger("poster-jobs", Connection = "AzureWebJobsStorage")] string message)
```

`QueueTrigger` reads straight from `AzureWebJobsStorage` with zero extra config, since it's the same storage account as `$STORAGE`. And the idempotency guard itself:

```csharp
if (props.Value.Metadata.TryGetValue("normalized", out var flag) && flag == "true")
    return;   // checked before any image work happens
```

One dependency worth knowing about if anything ever needs troubleshooting: the project pins `SixLabors.ImageSharp` to `2.1.9` rather than the current release -- see Issues & Fixes for why.

Prerequisite either way: **Azure Functions Core Tools (v4)** must be installed locally (`func` on the PATH) to build and publish the project -- alongside `az` and `dotnet`, which were already assumed. Install via npm (cross-platform):

```powershell
npm install -g azure-functions-core-tools@4 --unsafe-perm true
```

Verify:

```powershell
func --version
```

### 3.4 Live demo — deploying

```powershell
cd poster-normalizer
```

```powershell
dotnet build
```

```powershell
func azure functionapp publish $FUNC
```

```powershell
az functionapp function list --name $FUNC --resource-group $RG -o table
```

**If the function list comes back empty, do not assume the publish failed** — see 3.5 and Issues & Fixes below before troubleshooting live in class.

### 3.5 Fix — Function App not registering (regional host issue)

The 3.2 command above provisions cleanly and `func azure functionapp publish` reports success, but `az functionapp function list` can come back completely empty, with the Portal's Activity log showing `Sync Web Apps Function Triggers` failing (`InternalServerError`) and the app itself returning `502 Bad Gateway`.

**Root cause, confirmed live:** a `westcentralus`-specific Function App host-startup failure on this subscription (`System.ObjectDisposedException` inside `Microsoft.Azure.WebJobs.JobHost.StartAsync`), first hit on 2026-08-30 — not `poster-normalizer`'s code, its packages, or the hosting plan tier (ruled out with a minimal no-dependency repro function and a hosting-plan swap). No matching incident on Azure's status page.

**Fix:** host the Function App's *compute* in `eastus`, keeping `$STORAGE` (`stestiamdev2`) in `westcentralus` unchanged — `AzureWebJobsStorage` and the `posters`/`poster-jobs` resources don't need to be co-located with the compute, Azure allows the cross-region reference, and the code needs zero changes (`Connection = "AzureWebJobsStorage"` is unaffected).

```powershell
$FUNCLOCATION = "eastus"    # Function App compute only -- $STORAGE stays in westcentralus, unchanged
```

```powershell
az functionapp delete --name $FUNC --resource-group $RG
```

```powershell
az functionapp create --name $FUNC --resource-group $RG --storage-account $STORAGE --consumption-plan-location $FUNCLOCATION --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --os-type Linux
```

```powershell
func azure functionapp publish $FUNC
```

```powershell
az functionapp function list --name $FUNC --resource-group $RG -o table
```

This time `NormalizePoster` showed up correctly.

**If this region issue has since cleared up:** the 3.2/3.4 commands work as originally written, with `$FUNCLOCATION` simply left equal to `$LOCATION` — confirm by checking the Function App's host status after 3.4.

### 3.6 Live demo — wiring Event Grid and testing end-to-end

Register the Event Grid provider (first use this course) and create the subscription directly against the storage account — Azure creates the implicit system topic automatically, no separate step needed:

```powershell
az provider register --namespace Microsoft.EventGrid
```

```powershell
az provider show --namespace Microsoft.EventGrid --query registrationState -o tsv
```

```powershell
$STORAGE_ID = az storage account show --name $STORAGE --resource-group $RG --query id -o tsv
```

```powershell
$QUEUE_ID = "$STORAGE_ID/queueServices/default/queues/poster-jobs"
```

```powershell
az eventgrid event-subscription create --name poster-created-sub --source-resource-id $STORAGE_ID --endpoint-type storagequeue --endpoint $QUEUE_ID --included-event-types Microsoft.Storage.BlobCreated --subject-begins-with "/blobServices/default/containers/posters/"
```

Test with an existing poster rather than a new file — copying an existing blob to a new name in the same container fires a fresh `BlobCreated` event just like a real upload:

```powershell
az storage blob list --account-name $STORAGE --auth-mode login --container-name posters --query "[].name" -o table
```

```powershell
$BLOB_NAME = az storage blob list --account-name $STORAGE --auth-mode login --container-name posters --query "[0].name" -o tsv
```

```powershell
az storage blob copy start --account-name $STORAGE --auth-mode login --destination-container posters --destination-blob test-normalize-01.jpg --source-container posters --source-blob $BLOB_NAME
```

Give it 15-20 seconds, then confirm the Function actually ran:

```powershell
az storage blob show --account-name $STORAGE --auth-mode login --container-name posters --name test-normalize-01.jpg --query "{width:properties.contentLength, metadata:metadata}" -o json
```

```powershell
az storage message peek --account-name $STORAGE --auth-mode key --queue-name poster-jobs
```

Live result: `metadata: { normalized: "true" }` on the blob, and an empty array from the queue peek — the message was consumed and deleted after successful processing. The Function's own overwrite of the blob fires a second `BlobCreated` event through the same subscription; that second invocation is the idempotency guard's real test, and since nothing looped and the queue settled to empty, it held.

#### Issues & Fixes — Topic 3

- **`func new --template "Azure Queue Storage trigger"` fails:** `"Unknown template 'AzureQueueStoragetrigger'."` The C# isolated-worker templates use short aliases, not the friendly display name. Use `--template "Queue trigger"` instead.
- **`dotnet add package SixLabors.ImageSharp` (latest) then `func azure functionapp publish` fails:** `"No Six Labors license found."` ImageSharp moved to a commercial dual-license model (the Six Labors Split License) starting at v3.0. The free tier likely covers a personal course build, but claiming it means a Six Labors account, a license key, and wiring `SixLaborsLicenseKey` into the project — an extra moving part not worth depending on for a live class. Pinned to `2.1.9` instead, the last Apache-2.0 version, same API. This does carry two known NuGet-audit vulnerabilities (`GHSA-2cmq-823j-5qj8` high, `GHSA-rxmq-m78w-7wmc` moderate, both image-parsing DoS-type issues) — accepted as low risk for a training Function processing course-controlled images on throwaway infrastructure, not a production public-facing service. Revisit if this ever moves beyond a training context.
- **`az functionapp function list` comes back empty right after a successful `func azure functionapp publish`:** a West Central US-specific Function App host-startup failure on this subscription (confirmed live, 2026-08-30) — see 3.5 for symptoms and the `eastus` fix. Check whether it's cleared up before relying on the region workaround in a future run.
- **`az storage message peek --account-name $STORAGE --auth-mode login --queue-name poster-jobs` fails:** `"You do not have the required permissions."` Storage *data-plane* operations (reading blobs, peeking queue messages) need their own RBAC role (`Storage Queue Data Reader`/`Contributor`) separate from the Blob Data roles already granted, and separate from being the resource owner at the ARM level — a real Azure quirk. Fastest fix for a one-off check: `--auth-mode key` instead of `login`.
