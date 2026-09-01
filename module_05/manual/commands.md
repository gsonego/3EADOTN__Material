# Module 5 — Commands

## Topic 1 — API Management

```powershell
az provider register --namespace Microsoft.ApiManagement
```

```powershell
az provider show --namespace Microsoft.ApiManagement --query "registrationState" -o tsv
```

```powershell
$APIM = "apim-estiam-dev-2"
```

```powershell
$EMAIL = "gsonego1@outlook.com"
```

```powershell
az apim create --name $APIM --resource-group $RG --publisher-name "Estiam" --publisher-email "$EMAIL" --sku-name Consumption --location $LOCATION
```

```powershell
az apim show --name $APIM --resource-group $RG --query "{gatewayUrl:gatewayUrl, publicIPAddresses:publicIpAddresses}" -o json
```

```powershell
$FQDN = az containerapp show --name $APP --resource-group $RG --query "properties.configuration.ingress.fqdn" -o tsv
```

```powershell
az apim api import --resource-group $RG --service-name $APIM --path catalog --api-id catalog-api --specification-format OpenApi --specification-url "https://$FQDN/swagger/v1/swagger.json" --service-url "https://$FQDN"
```

```powershell
Invoke-WebRequest -Uri "https://$APIM.azure-api.net/catalog/titles" -Method GET
```

```powershell
$SUBID = az account show --query id -o tsv
```

```powershell
az rest --uri "/subscriptions/$SUBID/resourceGroups/$RG/providers/Microsoft.ApiManagement/service/$APIM/subscriptions?api-version=2022-08-01" -o json
```

```powershell
$KEY = az rest --method post --uri "/subscriptions/$SUBID/resourceGroups/$RG/providers/Microsoft.ApiManagement/service/$APIM/subscriptions/master/listSecrets?api-version=2022-08-01" --query primaryKey -o tsv
```

```powershell
Invoke-WebRequest -Uri "https://$APIM.azure-api.net/catalog/titles" -Headers @{ "Ocp-Apim-Subscription-Key" = $KEY }
```

Portal: APIs -> catalog-api -> Design -> Inbound processing -> code editor.

```xml
<rate-limit calls="3" renewal-period="30" />
```

```xml
<set-header name="X-Gateway" exists-action="override">
    <value>apim-estiam-dev-2</value>
</set-header>
```

```powershell
1..4 | ForEach-Object {
    $r = Invoke-WebRequest -Uri "https://$APIM.azure-api.net/catalog/titles" -Headers @{ "Ocp-Apim-Subscription-Key" = $KEY } -SkipHttpErrorCheck
    "$_`: $($r.StatusCode)"
}
```

## Topic 2 — Events vs Messages

No commands this topic -- concept only, nothing built live.

## Topic 3 — The Build: Poster Normalization

Prerequisite: Azure Functions Core Tools (v4) installed locally (`func` on the PATH), alongside `az` and `dotnet`.

```powershell
npm install -g azure-functions-core-tools@4 --unsafe-perm true
```

```powershell
func --version
```

```powershell
az storage queue create --name poster-jobs --account-name $STORAGE --auth-mode login
```

```powershell
$FUNC = "func-estiam-dev-2"    # must be globally unique across Azure
$FUNCLOCATION = "eastus"       # Function App compute only -- see manual 3.5. Try $LOCATION first; fall back to this if the function list comes back empty after publish.
```

```powershell
az functionapp create --name $FUNC --resource-group $RG --storage-account $STORAGE --consumption-plan-location $FUNCLOCATION --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --os-type Linux
```

The Function project ships ready-built in `materials/module_05/poster-normalizer/` -- no scaffolding or package install needed live (manual 3.3 covers what the code does and why `SixLabors.ImageSharp` is pinned to `2.1.9`).

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

If the function list comes back empty, see manual section 3.5 before troubleshooting -- likely fix is deleting and recreating the app with `$FUNCLOCATION` set to a different region than `$LOCATION`.

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

```powershell
az storage blob list --account-name $STORAGE --auth-mode login --container-name posters --query "[].name" -o table
```

```powershell
$BLOB_NAME = az storage blob list --account-name $STORAGE --auth-mode login --container-name posters --query "[0].name" -o tsv
```

```powershell
az storage blob copy start --account-name $STORAGE --auth-mode login --destination-container posters --destination-blob test-normalize-01.jpg --source-container posters --source-blob $BLOB_NAME
```

```powershell
az storage blob show --account-name $STORAGE --auth-mode login --container-name posters --name test-normalize-01.jpg --query "{width:properties.contentLength, metadata:metadata}" -o json
```

```powershell
az storage message peek --account-name $STORAGE --auth-mode key --queue-name poster-jobs
```
