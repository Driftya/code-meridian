FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/McpServer/CodeMeridian.McpServer.csproj",          "src/McpServer/"]
COPY ["src/Application/CodeMeridian.Application.csproj",      "src/Application/"]
COPY ["src/Core/CodeMeridian.Core.csproj",                    "src/Core/"]
COPY ["src/Infrastructure/CodeMeridian.Infrastructure.csproj", "src/Infrastructure/"]
COPY ["src/Sdk/CodeMeridian.Sdk.csproj",                      "src/Sdk/"]

RUN dotnet restore "src/McpServer/CodeMeridian.McpServer.csproj"

COPY . .
WORKDIR "/src/src/McpServer"
RUN dotnet publish "CodeMeridian.McpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "CodeMeridian.McpServer.dll"]
