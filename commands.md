# Commands

```powershell
az group list --output table

az provider register --namespace <Provider.Namespace>

az account list-locations --query "[].{Name:name, DisplayName:displayName}" --output table

az provider show --namespace <Provider.Namespace> --query registrationState --output tsv
```
