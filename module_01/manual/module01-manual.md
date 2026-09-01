# Module 1 — Develop Azure Computing Solutions

### Student Manual

Modern Enterprise Software Engineering — Day 1, Morning.

Default region used throughout this guide: **`westcentralus`**. Other regions may be used if necessary. If a command fails because that region rejects a specific resource, see the _Issues & Fixes_ note at the end of that section for how to pick another one. Confirm Docker Desktop is running before class — Topics 1 and 2 both need a local `docker build`.

---

## 0. Azure Subscription

You need your own Azure subscription — there is no shared subscription for the course.

| Option                 | Cost                                                        | Notes                                                                                                                     |
| ---------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| **Azure for Students** | $100 credit, 12 months, no credit card                      | Use this if you have a valid academic email                                                                               |
| **Free account**       | $200 credit for 30 days + 12 months of select free services | Requires a credit card for identity verification                                                                          |
| **Pay-As-You-Go**      | Billed per use, no credit                                   | Fallback if Free/Student hits a limit — upgrading from Free keeps remaining credit and does not trigger immediate charges |

Sign up at azure.microsoft.com (or the Student page for Azure for Students), complete verification, and confirm the subscription is active in the Portal.

**NOTE: Delete your resource groups at the end of the 3-day course** — this conserves your credit. You'll keep working inside the same resource group(s) throughout the course, so there's no need to delete anything mid-course.

#### Issues & Fixes — Subscription

- Some resource types show **0 quota** on fresh Free/Student subscriptions in certain regions, blocking creation even though the region itself is allowed. Fix: try a different region first; if that fails, upgrade to Pay-As-You-Go (Portal → Subscriptions → your subscription → Upgrade). This does not forfeit remaining free credit or trigger immediate charges.

---

## 1. Azure CLI

The `az` command line tool talks to Azure without using the Portal. Commands follow the pattern:

```powershell
az <group> <action> --parameters...
```

### 1.1 Install

Download from **aka.ms/installazurecliwindows** (Windows) or the equivalent installer for your OS, then verify:

```powershell
az --version
```

### 1.2 Log in and confirm your subscription

Signs you into Azure — opens a browser to complete the sign-in:

```powershell
az login
```

Shows the currently active subscription:

```powershell
az account show
```

Lists every subscription you can access:

```powershell
az account list --output table
```

Switches the active subscription, if you have more than one:

```powershell
az account set --subscription "<name-or-id>"
```

#### Issues & Fixes — Azure CLI

- On Windows, some installers default to a **32-bit** CLI. It runs fine at first but later fails to install certain extensions with an error like `pip ... Cannot import 'maturin'`. Fix: uninstall and reinstall the **64-bit** CLI from the same link; `az --version` should not show `(x86)` anywhere in the Python path.

---

## 2. Resource Groups

A **resource group (RG)** is a container for related resources sharing one lifecycle. Everything you create in this course goes into the same resource group, and it stays up for the whole 3-day course — you only delete it at the very end (see the note in Section 0).

> **Naming convention:** the first time this manual names a new resource, store it in a PowerShell variable and reuse the variable in every command after that — one place to fix a name if Azure rejects it (this matters most for globally-unique names like the ACR and Web App below). Define each variable right before its first use, not earlier. These variables only last for the current terminal session — if you close it and come back tomorrow, re-run the assignments (with the same values) before continuing.

Sets the region and resource group name used for the rest of the course:

```powershell
$LOCATION = "westcentralus"
```

```powershell
$RG = "rg-estiam-dev-2"
```

Creates the resource group everything in this module will live in:

```powershell
az group create --name $RG --location $LOCATION
```

Lists every resource group in the subscription, to confirm it was created:

```powershell
az group list --output table
```

#### Issues & Fixes — Resource Groups

- The RG's own `--location` is just metadata — resources created inside it can target a different region. If a specific resource type rejects your default region (`RequestDisallowedByAzure ... not accepting new customers`), change the `--location` on that resource's create command, not the RG.
- To find valid region names: `az account list-locations --query "[].{Name:name, DisplayName:displayName}" --output table`.

---

## 3. Resource Providers

Azure requires each resource **type** to be registered on your subscription before you can create it. New subscriptions often have common providers unregistered. You'll register each provider right before the topic that first needs it — there's nothing to register yet at this point, since no specific resource type has come up. The pattern looks like this:

```powershell
az provider register --namespace <Provider.Namespace>
```

```powershell
az provider show --namespace <Provider.Namespace> --query registrationState --output tsv
```

The `show` command should return `Registered`. If it says `Registering`, wait a minute and check again.

#### Issues & Fixes — Resource Providers

- Typical error: `MissingSubscriptionRegistration`, naming the provider directly.
- Confusing variant: an unregistered provider can surface as `SubscriptionNotFound`, even though `az account show` / `az account list` / `az group list` all look correct — most likely right after a Free→Pay-As-You-Go conversion (backend propagation lag). If you see `SubscriptionNotFound` but your account/subscription checks out, register the provider the error is most likely about anyway.

---

## 4. Topic 1 — Deploy Applications with Azure App Service

### 4.1 Concept summary

| Option                              | Scale to zero?                | Orchestration   | Best for                                                                    | Trade-off                                            |
| ----------------------------------- | ----------------------------- | --------------- | --------------------------------------------------------------------------- | ---------------------------------------------------- |
| **Azure Container Apps (ACA)**      | Yes (KEDA)                    | Managed for you | Modern default: APIs, microservices, event-driven workloads                 | Less low-level control than AKS                      |
| **Azure Container Instances (ACI)** | No                            | None            | One-off jobs, burst compute, CI/CD agents                                   | No autoscaling, load balancing, or service discovery |
| **Azure Kubernetes Service (AKS)**  | No                            | Full Kubernetes | Complex multi-service systems                                               | Most powerful, most operational overhead             |
| **App Service (containers)**        | No (Basic+ keeps ≥1 instance) | None            | Teams wanting App Service's deployment/slots model, packaged as a container | Tied to App Service plan/tier limits                 |

This topic starts with **App Service** — the classic PaaS option, familiar if you've deployed a web app before. Topic 2 then shows the same idea on **Container Apps**, the modern serverless option, plus a couple of capabilities that only make sense there.

An **App Service Plan** is the compute (VM size + scale settings) your app(s) run on — you provision and pay for it directly, unlike Container Apps' consumption-based model.

### 4.2 Live demo — build the Catalog UI and deploy it to Azure App Service

This module's demo app is a small movie/show catalog (title, genre, year, description, poster image). The **Catalog UI** is an ASP.NET Core MVC app; the **Catalog API** it talks to (Section 5) is a separate ASP.NET Core Web API. Both get built into container images and pushed to a shared **Azure Container Registry (ACR)** — your private registry, created once here since this is the first topic that needs it.

Sets the App Service Plan name for this topic:

```powershell
$PLAN = "asp-estiam-dev-2"
```

Creates the App Service Plan everything in this topic runs on (Linux, Basic tier — enough for a single demo app):

```powershell
az appservice plan create --resource-group $RG --name $PLAN --sku B1 --is-linux --location $LOCATION
```

Registers the provider and creates the registry (shared by both this topic and Section 5 — you won't need to create it again there):

```powershell
az provider register --namespace Microsoft.ContainerRegistry
```

Sets the ACR name — it must be **globally unique** across all of Azure (alphanumeric only, no hyphens); if the create command below fails because the name is taken, pick a different value here and re-run:

```powershell
$ACR = "acrestiamdev2"
```

```powershell
az acr create --resource-group $RG --name $ACR --sku Basic --location $LOCATION
```

Logs the local Docker CLI in to your ACR, then builds and pushes the Catalog UI image (from the `catalog-ui` project folder, which has its own `Dockerfile`):

```powershell
az acr login --name $ACR
```

```powershell
docker build -t "${ACR}.azurecr.io/catalog-ui:v1" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-ui:v1"
```

App Service needs credentials to pull from your (private) ACR — turns on the registry's admin account:

```powershell
az acr update --name $ACR --admin-enabled true
```

Sets the Web App name — also **globally unique** (it becomes part of a public URL); pick a different value here if the create command below rejects it:

```powershell
$WEBAPP = "webapp-estiam-dev-2"
```

Creates the Web App pointed at the pushed image:

```powershell
az webapp create --name $WEBAPP --plan $PLAN --resource-group $RG --deployment-container-image-name "${ACR}.azurecr.io/catalog-ui:v1"
```

`az webapp create` alone doesn't carry registry credentials — wires those up separately so App Service can actually pull the (private) image. The password is fetched live and passed straight through, not stored in a variable — this is the only place this module needs it for App Service:

```powershell
az webapp config container set --name $WEBAPP --resource-group $RG --container-image-name "${ACR}.azurecr.io/catalog-ui:v1" --container-registry-url "https://${ACR}.azurecr.io" --container-registry-user $ACR --container-registry-password (az acr credential show --name $ACR --resource-group $RG --query "passwords[0].value" --output tsv)
```

Prints the app's URL:

```powershell
echo "https://${WEBAPP}.azurewebsites.net"
```

Open it in a browser — you should see the Catalog UI (it can take a minute the first time, while App Service pulls the image). Its endpoint settings (top-right pill) aren't pointed anywhere yet — that's expected until Section 5 deploys the Catalog API.

#### Issues & Fixes — Topic 1

- **0 vCPU quota** can block App Service Plan creation even in an allowed region. Try a different region first (per section 2); if that fails, upgrade to Pay-As-You-Go.
- `az webapp up` is deprecated and will try to auto-detect a runtime from your folder — it can fail on an empty folder, or is simply outdated. Use the explicit `az appservice plan create` + `az webapp create`/`config container set` pattern above instead.

---

## 5. Topic 2 — Implement Containerized Solutions

### 5.1 Concept summary

**KEDA** is what powers Container Apps' scale-to-zero: with no traffic, replicas drop to 0; the first request after idle pays a short cold-start cost to spin one back up.

### 5.2 Live demo — build the Catalog API and deploy it to Azure Container Apps

This topic reuses the same ACR from Section 4 — no need to create it again.

Installs the CLI extension that adds the `containerapp` command group:

```powershell
az extension add --name containerapp --upgrade
```

Registers the providers this topic needs — the Container Apps platform itself, and the Log Analytics backend it logs to:

```powershell
az provider register --namespace Microsoft.App
```

```powershell
az provider register --namespace Microsoft.OperationalInsights
```

Builds and pushes the Catalog API image (from the `catalog-api` project folder, which has its own `Dockerfile`) — reuses the `az acr login` session from Section 4 if it's still active in this terminal, otherwise repeat that command first:

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v1" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v1"
```

Sets the Container Apps environment name:

```powershell
$ENVNAME = "env-estiam-dev-2"
```

Creates the Container Apps environment — the shared boundary (networking, logging) that container apps run inside:

```powershell
az containerapp env create --name $ENVNAME --resource-group $RG --location $LOCATION
```

If you don't pass a Log Analytics workspace, Azure generates one automatically (named something like `workspace-<rg><random>`) — that's expected, not an error.

Sets the Container App name:

```powershell
$APP = "app-estiam-dev-2"
```

Container Apps needs the same kind of registry credentials as Section 4's Web App did. Section 4 fetched the password inline since it's only needed there once; this command is already long, so it's worth its own variable here instead:

```powershell
$ACR_PASSWORD = az acr credential show --name $ACR --resource-group $RG --query "passwords[0].value" --output tsv
```

Deploys the Catalog API image, with external ingress so it gets a public URL, using that credential so Container Apps can pull the (private) image:

```powershell
az containerapp create --name $APP --resource-group $RG --environment $ENVNAME --image "${ACR}.azurecr.io/catalog-api:v1" --target-port 8080 --ingress external --registry-server "${ACR}.azurecr.io" --registry-username $ACR --registry-password $ACR_PASSWORD --query properties.configuration.ingress.fqdn
```

`--target-port 8080`, not 80 — .NET 8's container base images listen on 8080 by default. If you ever swap an _existing_ container app onto an image with a different listening port, update the ingress separately (`az containerapp ingress update --target-port <port>`) — the port isn't inferred from the image, so a mismatch there causes the revision to fail activation.

Open the returned FQDN in a browser — you should see the Catalog API's health response (JSON: app name, version, timestamp).

Now open the Catalog UI you deployed in Section 4 and point its endpoint settings at this Container App's FQDN — the catalog should load (though most actions still 404 until later modules add Cosmos DB persistence to the API).

Wait a few idle minutes, then check Portal → your app → **Scale/Metrics**: replica count should drop to 0 — something App Service's Basic tier can't do.

### 5.3 Advanced — revisions and traffic splitting

On Standard tier or above, App Service can use deployment slots for blue/green deployments. We won't create a slot in this lab because we're using the cheaper B1 tier.

Container Apps' answer is a **revision** — each `az containerapp update` (or `create`) with a changed image/config creates a new, independently-addressable revision (with its own URL too, so blue/green works here the same way: validate it in isolation, then flip 100% traffic over in one step). But revisions also unlock something App Service slots can't do at all: a **canary rollout** — splitting a percentage of _real_ traffic to the new revision before fully committing. That's a strictly different, stronger kind of validation than blue/green's out-of-band testing: it catches things that only show up under real production load and real edge-case inputs, at the cost of some live users being part of the test. This lab demos the canary version specifically, since it's the genuinely new capability.

One platform behaviour worth knowing, because it is the opposite of what most people expect: once the app is in **multiple** revision mode, deploying a new revision (via `update`) brings it up at **0% of traffic**. The revision is running and healthy, but no user reaches it until you assign it a weight. Revision lifecycle (single vs. multiple mode) and traffic routing are two independent settings — switching to multiple mode keeps old revisions alive, and traffic stays where it already was. So a canary rollout is necessarily two steps: deploy, then reweight. That is a safety net, not a limitation — a bad image cannot take production down just by being deployed. (In **single** revision mode the behaviour is different: the new revision replaces the old one and takes all traffic, which is exactly why `set-mode multiple` has to come first.)

To avoid live-editing code mid-demo, `v2` is a **separate project folder** (`catalog-api-v2`, a copy of `catalog-api` with the version bumped and one addition: a static, in-memory `GET /titles` list — real Cosmos DB-backed persistence comes in Module 2). Build and push it as its own tag:

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v2" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v2"
```

Switches the app into **multiple** revision mode, so old and new revisions can run side by side instead of the new one replacing the old immediately:

```powershell
az containerapp revision set-mode --name $APP --resource-group $RG --mode multiple
```

The `--revision-suffix` you pass is exactly the revision's name suffix, so the new revision's full name is deterministic — no need to look it up:

```powershell
$NEW_REVISION = "${APP}--v2"
```

Deploys the v2 image as a new revision:

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v2" --revision-suffix v2
```

The new revision comes up at **0% traffic** — nothing changes for callers yet. Lists both revisions and their current traffic weight, so you can see the split before/after each step below:

```powershell
az containerapp revision list --name $APP --resource-group $RG --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight}" --output table
```

The old revision's name is auto-generated (not something you chose), so fetch it instead of reading it off the table above — it's whichever revision isn't `$NEW_REVISION`:

```powershell
$OLD_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "[?name!='$NEW_REVISION'].name | [0]" --output tsv
```

Splits traffic 50/50 between the old and new revisions:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$OLD_REVISION=50" "$NEW_REVISION=50"
```

Call the app's URL a few times in a row — the JSON `version` field should flip between `v1` and `v2` roughly evenly, and only the `v2` responses have a working `/titles` endpoint.

Once you're confident the new revision is healthy, shift all traffic to it — this is the Container Apps equivalent of an App Service slot swap:

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

Open the Catalog UI again (Section 4) — with all traffic now on `v2`, its poster grid should actually populate from `GET /titles` for the first time.

#### Issues & Fixes — Topic 2

- **ACR Tasks blocked on fresh/free subscriptions**: `az acr build` (building the image in the cloud, from source) fails with `TasksOperationsNotAllowed` (anti-abuse restriction, not fixable on the spot). This is why both labs build locally with `docker build` and `docker push` instead — that path isn't affected by the restriction.
- **Container app revision stuck in `ActivationFailed`** after swapping to a new image: almost always an ingress target-port mismatch — the app's ingress still expects the _previous_ image's port. Check with `az containerapp show --name $APP --resource-group $RG --query properties.configuration.ingress.targetPort` and fix with `az containerapp ingress update --target-port <port>` if it doesn't match what your image actually listens on.
- Region blocked on a specific resource even though the RG was created fine: try a different `--location` on that resource (see section 2's Issues & Fixes).
- `az containerapp revision set-mode` must run **before** deploying the second revision — deploying with a `--revision-suffix` while still in single mode replaces the old revision instead of running alongside it.

---

## Reference

- Find region names: `az account list-locations --query "[].{Name:name, DisplayName:displayName}" --output table`
- End-of-course cleanup (see the note in Section 0 — do **not** run this mid-course): `az group delete --name $RG --yes --no-wait`
