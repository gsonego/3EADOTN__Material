# expenses-api — deployed to Azure Container Apps (see Section 3.3 of the project brief).
# .NET 8 images listen on 8080, not 80 — a Container Apps ingress target port mismatch here
# doesn't error clearly, the new revision just sits in ActivationFailed. Make sure
# --target-port 8080 is set when you create/update the Container App.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ExpensesApi.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ExpensesApi.dll"]
