# Module 3 — Commands

## 2.2 Live demo — Key Vault + Managed Identity

```
az provider register --namespace Microsoft.KeyVault
```

```
az provider show --namespace Microsoft.KeyVault --query registrationState --output tsv
```

```
$KV = "kv-estiam-dev-2"
```

```
az keyvault create --name $KV --resource-group $RG --location $LOCATION --enable-rbac-authorization true
```

```
$MY_OBJECT_ID = az ad signed-in-user show --query id --output tsv
```

```
$VAULT_ID = az keyvault show --name $KV --resource-group $RG --query id --output tsv
```

```
az role assignment create --role "Key Vault Secrets Officer" --assignee $MY_OBJECT_ID --scope $VAULT_ID
```

```
$COSMOS_CONN = az cosmosdb keys list --type connection-strings --name $COSMOS --resource-group $RG --query "connectionStrings[0].connectionString" --output tsv
```

```
az keyvault secret set --vault-name $KV --name CosmosConnectionString --value $COSMOS_CONN
```

```
$APP_PRINCIPAL_ID = az containerapp identity assign --name $APP --resource-group $RG --system-assigned --query principalId --output tsv
```

```
az containerapp secret set --name $APP --resource-group $RG --secrets "cosmos-conn=keyvaultref:https://$KV.vault.azure.net/secrets/CosmosConnectionString,identityref:system"
```

```
az role assignment create --role "Key Vault Secrets User" --assignee $APP_PRINCIPAL_ID --scope $VAULT_ID
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
$STORAGE_CONN = az storage account show-connection-string --name $STORAGE --resource-group $RG --query connectionString --output tsv
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v5" --set-env-vars "CosmosDb__ConnectionString=secretref:cosmos-conn" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=$STORAGE_CONN" "BlobStorage__ContainerName=posters"
```

```
$NEW_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "sort_by(@, &properties.createdTime)[-1].name" --output tsv
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

## 2.3 Upgrade — Cosmos DB direct via Managed Identity

```
az cosmosdb sql role definition list --account-name $COSMOS --resource-group $RG --query "[].{Name:roleName, Id:id}" -o table
```

```
$COSMOS_ROLE_ID = az cosmosdb sql role definition list --account-name $COSMOS --resource-group $RG --query "[?roleName=='Cosmos DB Built-in Data Contributor'].id | [0]" --output tsv
```

```
az cosmosdb sql role assignment create --account-name $COSMOS --resource-group $RG --role-definition-id $COSMOS_ROLE_ID --principal-id $MY_OBJECT_ID --scope "/"
```

```
az cosmosdb sql role assignment create --account-name $COSMOS --resource-group $RG --role-definition-id $COSMOS_ROLE_ID --principal-id $APP_PRINCIPAL_ID --scope "/"
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v6" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v6"
```

```
$COSMOS_ENDPOINT = az cosmosdb show --name $COSMOS --resource-group $RG --query documentEndpoint --output tsv
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v6" --set-env-vars "CosmosDb__AccountEndpoint=$COSMOS_ENDPOINT" "CosmosDb__DatabaseName=CatalogDb" "CosmosDb__ContainerName=Titles" "BlobStorage__ConnectionString=$STORAGE_CONN" "BlobStorage__ContainerName=posters" --remove-env-vars "CosmosDb__ConnectionString"
```

```
$NEW_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "sort_by(@, &properties.createdTime)[-1].name" --output tsv
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

## 2.4 Upgrade — ACR pull via Managed Identity (closing a Module 1 gotcha)

**Container App**

```
$ACR_ID = az acr show --name $ACR --resource-group $RG --query id --output tsv
```

```
az role assignment create --role "AcrPull" --assignee $APP_PRINCIPAL_ID --scope $ACR_ID
```

```
az containerapp registry set --name $APP --resource-group $RG --server "${ACR}.azurecr.io" --identity system
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v6" --revision-suffix acrmi
```

**App Service**

```
$WEBAPP_PRINCIPAL_ID = az webapp identity assign --name $WEBAPP --resource-group $RG --query principalId --output tsv
```

```
az role assignment create --role "AcrPull" --assignee $WEBAPP_PRINCIPAL_ID --scope $ACR_ID
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
