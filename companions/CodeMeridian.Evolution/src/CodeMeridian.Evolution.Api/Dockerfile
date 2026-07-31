FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/CodeMeridian.Evolution.Api/CodeMeridian.Evolution.Api.csproj \
    --configuration Release \
    --output /app \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CodeMeridian.Evolution.Api.dll"]

