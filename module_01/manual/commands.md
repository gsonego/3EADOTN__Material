# Module 1 — Commands

Every CLI command that appears in the Module 1 manual, in the same order.

## Azure CLI

```
az --version
```

```
az login
```

```
az account show
```

```
az account list --output table
```

```
az account set --subscription "<name-or-id>"
```

## Resource Groups

```
$LOCATION = "westcentralus"
$RG = "rg-estiam-dev-2"
```

```
az group create --name $RG --location $LOCATION
```

```
az group list --output table
```

## Resource Providers

```
az provider register --namespace <Provider.Namespace>
```

```
az provider show --namespace <Provider.Namespace> --query registrationState --output tsv
```

## Topic 1 — Deploy Applications with Azure App Service

```
$PLAN = "asp-estiam-dev-2"
```

```
az appservice plan create --resource-group $RG --name $PLAN --sku B1 --is-linux --location $LOCATION
```

```
az provider register --namespace Microsoft.ContainerRegistry
$ACR = "acrestiamdev2"
az acr create --resource-group $RG --name $ACR --sku Basic --location $LOCATION
```

```
az acr login --name $ACR
docker build -t "${ACR}.azurecr.io/catalog-ui:v1" .
docker push "${ACR}.azurecr.io/catalog-ui:v1"
```

```
az acr update --name $ACR --admin-enabled true
```

```
$WEBAPP = "webapp-estiam-dev-2"
```

```
az webapp create --name $WEBAPP --plan $PLAN --resource-group $RG --deployment-container-image-name "${ACR}.azurecr.io/catalog-ui:v1"
```

```
az webapp config container set --name $WEBAPP --resource-group $RG --container-image-name "${ACR}.azurecr.io/catalog-ui:v1" --container-registry-url "https://${ACR}.azurecr.io" --container-registry-user $ACR --container-registry-password (az acr credential show --name $ACR --resource-group $RG --query "passwords[0].value" --output tsv)
```

```
echo "https://${WEBAPP}.azurewebsites.net"
```

## Topic 2 — Implement Containerized Solutions

```
az extension add --name containerapp --upgrade
```

```
az provider register --namespace Microsoft.App
```

```
az provider register --namespace Microsoft.OperationalInsights
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v1" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v1"
```

```
$ENVNAME = "env-estiam-dev-2"
```

```
az containerapp env create --name $ENVNAME --resource-group $RG --location $LOCATION
```

```
$APP = "app-estiam-dev-2"
```

```
$ACR_PASSWORD = az acr credential show --name $ACR --resource-group $RG --query "passwords[0].value" --output tsv
```

```
az containerapp create --name $APP --resource-group $RG --environment $ENVNAME --image "${ACR}.azurecr.io/catalog-api:v1" --target-port 8080 --ingress external --registry-server "${ACR}.azurecr.io" --registry-username $ACR --registry-password $ACR_PASSWORD --query properties.configuration.ingress.fqdn
```

```
docker build -t "${ACR}.azurecr.io/catalog-api:v2" .
```

```
docker push "${ACR}.azurecr.io/catalog-api:v2"
```

```
az containerapp revision set-mode --name $APP --resource-group $RG --mode multiple
```

```
$NEW_REVISION = "${APP}--v2"
```

```
az containerapp update --name $APP --resource-group $RG --image "${ACR}.azurecr.io/catalog-api:v2" --revision-suffix v2
```

```
az containerapp revision list --name $APP --resource-group $RG --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight}" --output table
```

```
$OLD_REVISION = az containerapp revision list --name $APP --resource-group $RG --query "[?name!='$NEW_REVISION'].name | [0]" --output tsv
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$OLD_REVISION=50" "$NEW_REVISION=50"
```

```
az containerapp ingress traffic set --name $APP --resource-group $RG --revision-weight "$NEW_REVISION=100"
```

## Reference

```
az account list-locations --query "[].{Name:name, DisplayName:displayName}" --output table
```

```
az group delete --name $RG --yes --no-wait
```
