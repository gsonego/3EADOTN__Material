# expenses-ui — deployed to Azure App Service as a container (see Section 3.3 of the
# project brief). App Service for Linux containers expects port 80 by default; this
# image listens on 8080 instead, so set the app setting WEBSITES_PORT=8080 on the Web
# App or the container will look "deployed" but never respond.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ExpensesUi.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ExpensesUi.dll"]
