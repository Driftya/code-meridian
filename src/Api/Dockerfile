# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Api/CodeMeridian.Api.csproj",                 "src/Api/"]
COPY ["src/Application/CodeMeridian.Application.csproj", "src/Application/"]
COPY ["src/Core/CodeMeridian.Core.csproj",               "src/Core/"]
COPY ["src/Infrastructure/CodeMeridian.Infrastructure.csproj", "src/Infrastructure/"]

RUN dotnet restore "src/Api/CodeMeridian.Api.csproj"

COPY . .
WORKDIR "/src/src/Api"
RUN dotnet publish "CodeMeridian.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080

# Run as non-root for security
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CodeMeridian.Api.dll"]
