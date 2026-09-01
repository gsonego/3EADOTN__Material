# Module 6 — Commands

Every command from the Module 6 manual, in order, without output. Run in **PowerShell** (Git Bash rewrites arguments starting with `/`).

**Note on this module:** students run no commands at all — their entire lab happens in a browser. Everything below is trainer setup, run once before class. It is kept here so the module is reproducible on a fresh subscription.

Re-run `materials/variables.ps1` first if this is a new terminal session.

---

## 1. Azure OpenAI — provision the model endpoint

Register the provider (not registered by default on a fresh subscription).

```powershell
az provider register --namespace Microsoft.CognitiveServices
```

```powershell
az provider show --namespace Microsoft.CognitiveServices --query registrationState -o tsv
```

Check which regions offer Azure OpenAI at all.

```powershell
az cognitiveservices account list-skus --kind OpenAI --query "[].locations[]" -o tsv | Sort-Object -Unique
```

Check which models are deployable there — and their lifecycle status. A model can be listed and still be undeployable.

```powershell
az cognitiveservices model list --location swedencentral --query "[?kind=='OpenAI' && contains(model.name,'gpt') && model.lifecycleStatus!='Deprecating'].{name:model.name, version:model.version, status:model.lifecycleStatus, skus:join(',', model.skus[].name)}" -o table
```

Check what quota is actually granted — per model **and** per SKU.

```powershell
az cognitiveservices usage list --location $AOAILOCATION -o json | ConvertFrom-Json | Where-Object { $_.limit -gt 0 } | Select-Object @{n='quota';e={$_.name.value}}, currentValue, limit | Sort-Object quota | Format-Table -AutoSize
```

Variables. `$AOAILOCATION` is deliberately separate from `$LOCATION`.

```powershell
$AOAILOCATION = "swedencentral"
$AOAI         = "aoai-estiam-dev-2"
$AOAIDEPLOY   = "gpt-5-mini"
$AOAIMODELVER = "2025-08-07"
```

Create the account. `--custom-domain` is what produces the `https://<name>.openai.azure.com` endpoint form.

```powershell
az cognitiveservices account create --name $AOAI --resource-group $RG --location $AOAILOCATION --kind OpenAI --sku S0 --custom-domain $AOAI --yes
```

Deploy the model. Capacity is a rate ceiling, not a charge.

```powershell
az cognitiveservices account deployment create --name $AOAI --resource-group $RG --deployment-name $AOAIDEPLOY --model-name $AOAIDEPLOY --model-version $AOAIMODELVER --model-format OpenAI --sku-name GlobalStandard --sku-capacity 100
```

Endpoint and key. **One assignment per line** — inside a backtick-continued block these come back empty with no error.

```powershell
$AOAIENDPOINT = az cognitiveservices account show --name $AOAI --resource-group $RG --query properties.endpoint -o tsv
```

```powershell
$AOAIKEY = az cognitiveservices account keys list --name $AOAI --resource-group $RG --query key1 -o tsv
```

---

## 2. The key — Key Vault and managed identity

Store the key.

```powershell
az keyvault secret set --vault-name $KV --name "azure-openai-key" --value $AOAIKEY --query id -o tsv
```

Grant the Container App's existing system-assigned identity data-plane access. Being subscription Owner is not enough — this is a separate role.

```powershell
$PRINCIPAL = az containerapp show --name $APP --resource-group $RG --query identity.principalId -o tsv
```

```powershell
$KVID = az keyvault show --name $KV --query id -o tsv
```

```powershell
az role assignment create --assignee $PRINCIPAL --role "Key Vault Secrets User" --scope $KVID
```

---

## 3. Deploy `catalog-api:v9`

```powershell
az acr login --name $ACR
```

From `materials/module_06/catalog-api-v9/`:

```powershell
docker build -t "${ACR}.azurecr.io/catalog-api:v9" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-api:v9"
```

The secret is a *reference* to Key Vault resolved by the managed identity — not a copy of the value.

```powershell
az containerapp secret set --name $APP --resource-group $RG --secrets "azure-openai-key=keyvaultref:https://$KV.vault.azure.net/secrets/azure-openai-key,identityref:system"
```

`--set-env-vars` adds and updates only what is named. `--replace-env-vars` would wipe the existing Cosmos and Blob settings.

```powershell
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v9" --revision-suffix v9 --set-env-vars "AzureOpenAI__Endpoint=https://aoai-estiam-dev-2.openai.azure.com/" "AzureOpenAI__Deployment=gpt-5-mini" "AzureOpenAI__ApiVersion=2025-04-01-preview" "AzureOpenAI__ApiKey=secretref:azure-openai-key"
```

Check the new revision actually started — in multiple-revision mode a broken one fails quietly while the old one keeps serving.

```powershell
az containerapp revision list --name $APP --resource-group $RG --query "[].{name:name, active:properties.active, state:properties.runningState, traffic:properties.trafficWeight}" -o table
```

New revisions start at 0% traffic. This is the step that actually ships it.

```powershell
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$APP--v9=100"
```

---

## 4. APIM — register the new operation

The gateway does not learn new endpoints when the backend gains them. Adding the single operation rather than re-importing the Swagger, so the `rate-limit` and `set-header` policies from Module 5 are left untouched.

```powershell
az apim api operation list --resource-group $RG --service-name $APIM --api-id catalog-api --query "[].{name:name, method:method, url:urlTemplate}" -o table
```

```powershell
az apim api operation create --resource-group $RG --service-name $APIM --api-id catalog-api --operation-id "AskAssistant" --display-name "AskAssistant" --method POST --url-template "/assistant"
```

---

## 5. Deploy `catalog-ui:v4`

`catalog-ui` is one project grown in place since Module 1 — only the image tag increments. Run from `materials/module_01/catalog-ui/`.

```powershell
docker build -t "${ACR}.azurecr.io/catalog-ui:v4" .
```

```powershell
docker push "${ACR}.azurecr.io/catalog-ui:v4"
```

```powershell
az webapp config container set --name $WEBAPP --resource-group $RG --container-image-name "${ACR}.azurecr.io/catalog-ui:v4"
```

```powershell
az webapp restart --name $WEBAPP --resource-group $RG
```

---

## 6. Pre-class verification

```powershell
$FQDN = az containerapp show --name $APP --resource-group $RG --query properties.configuration.ingress.fqdn -o tsv
```

```powershell
Invoke-RestMethod "https://$FQDN/" | Format-List
```

```powershell
$b = @{ question = "List 5 titles from your catalog with their release years."; grounded = $true } | ConvertTo-Json
```

```powershell
Invoke-RestMethod "https://$FQDN/assistant" -Method Post -Body $b -ContentType "application/json" | Select-Object grounded, contextTitles, answer | Format-List
```

If that returns a 502, it is almost always the Key Vault reference failing to resolve rather than the model call:

```powershell
az containerapp logs show --name $APP --resource-group $RG --revision "$APP--v9" --tail 40
```
