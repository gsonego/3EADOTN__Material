# Module 3 — Commands

## 2.2 Live demo — Key Vault + Managed Identity

```
az provider register --namespace Microsoft.KeyVault
```

```
az provider show --namespace Microsoft.KeyVault --query registrationState --output tsv
```

```
az keyvault create --name $KV --resource-group $RG --location $LOCATION --enable-rbac-authorization true
```

```
az ad signed-in-user show --query id --output tsv
```

```
az role assignment create --role "Key Vault Secrets Officer" --assignee <your-object-id> --scope <vault-resource-id>
```

```
az cosmosdb keys list --type connection-strings --name $COSMOS --resource-group $RG --query "connectionStrings[0].connectionString" --output tsv
```

```
az keyvault secret set --vault-name $KV --name CosmosConnectionString --value "<connection string>"
```

```
az containerapp identity assign --name $APP --resource-group $RG --system-assigned
```

```
az containerapp secret set --name $APP --resource-group $RG --secrets "cosmos-conn=keyvaultref:https://$KV.vault.azure.net/secrets/CosmosConnectionString,identityref:system"
```

```
az role assignment create --role "Key Vault Secrets User" --assignee <container-app-principal-id> --scope <vault-resource-id>
```

```
az acr login --name $ACR
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v5" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v5"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v5" --set-env-vars "CosmosDb__ConnectionString=secretref:cosmos-conn" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=<storage connection string>" "BlobStorage__ContainerName=posters"
```

```
curl "https://app-estiam-dev-2--0000005.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io/titles"
```

```
az containerapp revision list --name $APP --resource-group $RG --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight, Image:properties.template.containers[0].image}" --output table
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--0000005=100
```

```
curl "https://webapp-estiam-dev-2.azurewebsites.net/api/titles" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io"
```

## 2.3 Upgrade — Cosmos DB direct via Managed Identity

```
az cosmosdb sql role definition list --account-name $COSMOS --resource-group $RG --query "[].{Name:roleName, Id:id}" -o table
```

```
az cosmosdb sql role assignment create --account-name $COSMOS --resource-group $RG --role-definition-id <Cosmos DB Built-in Data Contributor id> --principal-id <your-object-id> --scope "/"
```

```
az cosmosdb sql role assignment create --account-name $COSMOS --resource-group $RG --role-definition-id <Cosmos DB Built-in Data Contributor id> --principal-id <container-app-principal-id> --scope "/"
```

```
dotnet add package Azure.Identity
```

```
dotnet build
```

```
dotnet run --urls http://localhost:5097
```

```
curl http://localhost:5097/titles
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v6" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v6"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v6" --set-env-vars "CosmosDb__AccountEndpoint=<cosmos account endpoint>" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=<storage connection string>" "BlobStorage__ContainerName=posters" --remove-env-vars "CosmosDb__ConnectionString"
```

```
curl "https://app-estiam-dev-2--0000006.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io/titles"
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--0000006=100
```

```
curl "https://webapp-estiam-dev-2.azurewebsites.net/api/titles" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io"
```

## 2.4 Upgrade — ACR pull via Managed Identity (closing a Module 1 gotcha)

**Container App**

```
az role assignment create --role "AcrPull" --assignee <container-app-principal-id> --scope <acr-resource-id>
```

```
az containerapp registry set --name $APP --resource-group $RG --server "${ACR}.azurecr.io" --identity system
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v6" --revision-suffix acrmi
```

```
curl "https://app-estiam-dev-2--acrmi.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io/titles"
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight app-estiam-dev-2--acrmi=100
```

**App Service**

```
az webapp identity assign --name $WEBAPP --resource-group $RG
```

```
az role assignment create --role "AcrPull" --assignee <webapp-principal-id> --scope <acr-resource-id>
```

```
az webapp config set --resource-group $RG --name $WEBAPP --acr-use-identity true --acr-identity "[system]"
```

```
az webapp config appsettings delete --name $WEBAPP --resource-group $RG --setting-names DOCKER_REGISTRY_SERVER_URL DOCKER_REGISTRY_SERVER_USERNAME DOCKER_REGISTRY_SERVER_PASSWORD
```

```
az acr update --name $ACR --admin-enabled false
```

```
az webapp restart --name $WEBAPP --resource-group $RG
```

```
curl "https://$WEBAPP.azurewebsites.net/api/titles" -H "X-Catalog-Base-Url: https://app-estiam-dev-2.delightfulgrass-32a51ddf.westcentralus.azurecontainerapps.io"
```
