# Module 2 — Commands

## Topic 1 — Azure Cosmos DB

```
az provider register --namespace Microsoft.DocumentDB
```

```
az provider show --namespace Microsoft.DocumentDB --query registrationState --output tsv
```

```
$COSMOS = "cosmos-estiam-dev-2"
```

```
az cosmosdb create --name $COSMOS --resource-group $RG --locations regionName=$LOCATION --default-consistency-level Session --kind GlobalDocumentDB
```

```
az cosmosdb sql database create --account-name $COSMOS --resource-group $RG --name CatalogDb
```

```
az cosmosdb sql container create --account-name $COSMOS --resource-group $RG --database-name CatalogDb --name Titles --partition-key-path "/genre" --throughput 400
```

```
$COSMOS_CONN = az cosmosdb keys list --type connection-strings --name $COSMOS --resource-group $RG --query "connectionStrings[0].connectionString" --output tsv
```

```
az acr login --name $ACR
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v3" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v3"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v3" --set-env-vars "CosmosDb__ConnectionString=$COSMOS_CONN" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles"
```

```
az containerapp revision list --name $APP --resource-group $RG --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight, Image:properties.template.containers[0].image}" --output table
```

```
$NEW_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "sort_by(@, &properties.createdTime)[-1].name" --output tsv
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

## Topic 2 — Azure Blob Storage

```
$STORAGE = "stestiamdev2"
```

```
az storage account create --name $STORAGE --resource-group $RG --location $LOCATION --sku Standard_LRS
```

```
az storage container create --name posters --account-name $STORAGE --auth-mode login
```

```
az storage blob upload --account-name $STORAGE --container-name posters --name hello.txt --file hello.txt --auth-mode login
```

```
$MY_OBJECT_ID = az ad signed-in-user show --query id --output tsv
```

```
$STORAGE_ID = az storage account show --name $STORAGE --resource-group $RG --query id --output tsv
```

```
az role assignment create --role "Storage Blob Data Contributor" --assignee $MY_OBJECT_ID --scope $STORAGE_ID
```

```
$BLOB_URL = az storage blob url --account-name $STORAGE --container-name posters --name hello.txt --output tsv
```

```
$SAS_EXPIRY = (Get-Date).ToUniversalTime().AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
```

```
$SAS_TOKEN = az storage blob generate-sas --account-name $STORAGE --container-name posters --name hello.txt --permissions r --expiry $SAS_EXPIRY --https-only --auth-mode login --as-user --output tsv
```

```
$FULL_URL = "$BLOB_URL`?$SAS_TOKEN"
```

```
Start-Process $FULL_URL
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v4" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v4"
```

```
$STORAGE_CONN = az storage account show-connection-string --name $STORAGE --resource-group $RG --query connectionString --output tsv
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v4" --set-env-vars "CosmosDb__ConnectionString=$COSMOS_CONN" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=$STORAGE_CONN" "BlobStorage__ContainerName=posters"
```

```
$NEW_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "sort_by(@, &properties.createdTime)[-1].name" --output tsv
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```
