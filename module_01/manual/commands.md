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
az group create --name rg-estiam-dev-2 --location westcentralus
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
az appservice plan create --resource-group rg-estiam-dev-2 --name asp-estiam-dev-2 --sku B1 --is-linux --location westcentralus
```

```
az provider register --namespace Microsoft.ContainerRegistry
az acr create --resource-group rg-estiam-dev-2 --name acrestiamdev2 --sku Basic --location westcentralus
```

```
az acr login --name acrestiamdev2
docker build -t acrestiamdev2.azurecr.io/catalog-ui:v1 .
docker push acrestiamdev2.azurecr.io/catalog-ui:v1
```

```
az acr update --name acrestiamdev2 --admin-enabled true
az acr credential show --name acrestiamdev2 --query "passwords[0].value" --output tsv
```

```
az webapp create --name webapp-estiam-dev-2 --plan asp-estiam-dev-2 --resource-group rg-estiam-dev-2 --deployment-container-image-name acrestiamdev2.azurecr.io/catalog-ui:v1
```

```
az webapp config container set --name webapp-estiam-dev-2 --resource-group rg-estiam-dev-2 --container-image-name acrestiamdev2.azurecr.io/catalog-ui:v1 --container-registry-url https://acrestiamdev2.azurecr.io --container-registry-user acrestiamdev2 --container-registry-password "<password from the previous command>"
```

## Topic 2 — Implement Containerized Solutions

```
az extension add --name containerapp --upgrade
```

```
az provider register --namespace Microsoft.App
az provider register --namespace Microsoft.OperationalInsights
```

```
docker build -t acrestiamdev2.azurecr.io/catalog-api:v1 .
docker push acrestiamdev2.azurecr.io/catalog-api:v1
```

```
az containerapp env create --name env-estiam-dev-2 --resource-group rg-estiam-dev-2 --location westcentralus
```

```
az containerapp create --name app-estiam-dev-2 --resource-group rg-estiam-dev-2 --environment env-estiam-dev-2 --image acrestiamdev2.azurecr.io/catalog-api:v1 --target-port 8080 --ingress external --registry-server acrestiamdev2.azurecr.io --registry-username acrestiamdev2 --registry-password "<password from Section 4>" --query properties.configuration.ingress.fqdn
```

```
docker build -t acrestiamdev2.azurecr.io/catalog-api:v2 .
docker push acrestiamdev2.azurecr.io/catalog-api:v2
```

```
az containerapp revision set-mode --name app-estiam-dev-2 --resource-group rg-estiam-dev-2 --mode multiple
```

```
az containerapp update --name app-estiam-dev-2 --resource-group rg-estiam-dev-2 --image acrestiamdev2.azurecr.io/catalog-api:v2 --revision-suffix v2
```

```
az containerapp revision list --name app-estiam-dev-2 --resource-group rg-estiam-dev-2 --query "[].{Revision:name, Active:properties.active, Traffic:properties.trafficWeight}" --output table
```

```
az containerapp ingress traffic set --name app-estiam-dev-2 --resource-group rg-estiam-dev-2 --revision-weight <old-revision>=50 <new-revision>=50
```

```
az containerapp ingress traffic set --name app-estiam-dev-2 --resource-group rg-estiam-dev-2 --revision-weight <new-revision>=100
```

## Reference

```
az account list-locations --query "[].{Name:name, DisplayName:displayName}" --output table
```

```
az group delete --name rg-estiam-dev-2 --yes --no-wait
```
