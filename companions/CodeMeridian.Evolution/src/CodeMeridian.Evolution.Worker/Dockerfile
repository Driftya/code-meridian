FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/CodeMeridian.Evolution.Worker/CodeMeridian.Evolution.Worker.csproj \
    --configuration Release \
    --output /app \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "CodeMeridian.Evolution.Worker.dll"]

