# Module 2 — Commands

## Topic 1 — Azure Cosmos DB

```
az provider register --namespace Microsoft.DocumentDB
```

```
az provider show --namespace Microsoft.DocumentDB --query registrationState --output tsv
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
az cosmosdb keys list --type connection-strings --name $COSMOS --resource-group $RG --query "connectionStrings[0].connectionString" --output tsv
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
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v3" --set-env-vars "CosmosDb__ConnectionString=<connection string>" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles"
```

```
az containerapp revision list --name $APP --resource-group $RG --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight, Image:properties.template.containers[0].image}" --output table
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--0000003=100
```

```
curl "https://webapp-estiam-dev-2.azurewebsites.net/api/titles" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io"
```

```
curl -X POST "https://webapp-estiam-dev-2.azurewebsites.net/api/titles" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io" -H "Content-Type: application/json" -d "{\"title\":\"Deep Current\",\"genre\":\"Documentary\",\"year\":2025,\"description\":\"Following a research vessel through the Pacific's deepest trenches.\"}"
```

## Topic 2 — Azure Blob Storage

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
az storage account show --name $STORAGE --resource-group $RG --query id --output tsv
```

```
az role assignment create --role "Storage Blob Data Contributor" --assignee <your-user-or-object-id> --scope <storage-account-resource-id>
```

```
az storage blob url --account-name $STORAGE --container-name posters --name hello.txt --output tsv
```

```
az storage blob generate-sas --account-name $STORAGE --container-name posters --name hello.txt --permissions r --expiry <UTC datetime> --https-only --auth-mode login --as-user --output tsv
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v4" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v4"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v4" --set-env-vars "CosmosDb__ConnectionString=<connection string>" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=<storage connection string>" "BlobStorage__ContainerName=posters"
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--0000004=100
```

```
curl -X POST "https://webapp-estiam-dev-2.azurewebsites.net/api/titles/<id>/poster" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io" -F "file=@poster.png;type=image/png"
```
