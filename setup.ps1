#!/usr/bin/env pwsh
# setup.ps1 — Creates the CodeMeridian solution and wires all projects together.
# Run once after cloning: ./setup.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "=== CodeMeridian Setup ===" -ForegroundColor Cyan

# -- Solution ------------------------------------------------------------------
if (-not (Test-Path "CodeMeridian.sln")) {
    dotnet new sln -n CodeMeridian
    Write-Host "Solution created." -ForegroundColor Green
}

# -- Add projects --------------------------------------------------------------
$projects = @(
    "src/Core/CodeMeridian.Core.csproj",
    "src/Application/CodeMeridian.Application.csproj",
    "src/Infrastructure/CodeMeridian.Infrastructure.csproj",
    "src/McpServer/CodeMeridian.McpServer.csproj",
    "src/Sdk/CodeMeridian.Sdk.csproj",
    "tools/Indexer/CodeMeridian.Indexer.csproj",
    "tools/RoslynIndexer/CodeMeridian.RoslynIndexer.csproj",
    "tests/CodeMeridian.Core.Tests/CodeMeridian.Core.Tests.csproj",
    "tests/CodeMeridian.Application.Tests/CodeMeridian.Application.Tests.csproj",
    "tests/CodeMeridian.Indexer.Tests/CodeMeridian.Indexer.Tests.csproj"
)

foreach ($project in $projects) {
    dotnet sln add $project
}
Write-Host "All projects added to solution." -ForegroundColor Green

# -- Restore & build -----------------------------------------------------------
Write-Host "Restoring packages..." -ForegroundColor Cyan
dotnet restore

Write-Host "Building solution..." -ForegroundColor Cyan
dotnet build --no-restore

# -- Next steps ----------------------------------------------------------------
Write-Host ""
Write-Host "=== Setup complete! ===" -ForegroundColor Green
Write-Host ""
Write-Host "How it works:" -ForegroundColor Yellow
Write-Host "  CodeMeridian is a pure MCP server — GitHub Copilot IS the reasoning engine."
Write-Host "  No LLM API key required. VS Code Copilot discovers CodeMeridian tools automatically."
Write-Host ""
Write-Host "Start CodeMeridian:" -ForegroundColor Cyan
Write-Host "  1. Copy .env.example -> .env"
Write-Host "  2. docker compose up -d              # starts Neo4j + CodeMeridian MCP server"
Write-Host "  3. Open VS Code in this workspace"
Write-Host "  4. GitHub Copilot will see 'CodeMeridian' in the MCP server list (via .vscode/mcp.json)"
Write-Host "  5. In Copilot chat, try: 'Use CodeMeridian to show the architectural overview'"
Write-Host ""
Write-Host "Connect your project:" -ForegroundColor Cyan
Write-Host "  # Option A — reference the SDK project directly"
Write-Host "  dotnet add reference /path/to/CodeMeridian/src/Sdk/CodeMeridian.Sdk.csproj"
Write-Host ""
Write-Host "  # Option B — tell Copilot to register your project agent"
Write-Host "  # In Copilot chat: 'register a project agent named MyApi at http://localhost:5001/ask'"
Write-Host ""
Write-Host "  # Option C — run the unified indexer on your codebase"
Write-Host "  dotnet run --project tools/Indexer -- <path-to-your-project> --project MyApi"
Write-Host "  dotnet run --project tools/Indexer -- C:\Projects\MyApi --project MyApi --clear"
Write-Host ""
Write-Host "  # Or install the indexer tool and use:"
Write-Host "  dotnet pack tools/Indexer -o artifacts/packages"
Write-Host "  dotnet tool install CodeMeridian.Indexer --global --add-source artifacts/packages"
Write-Host "  codemeridian index C:\Projects\MyApi --project MyApi"
Write-Host ""
Write-Host "  # Indexer options:"
Write-Host "  #   --project   <name>   Project context name (required)"
Write-Host "  #   --url / --CodeMeridian <url> CodeMeridian URL (default: http://localhost:5100)"
Write-Host "  #   --clear              Wipe existing knowledge before indexing"
Write-Host "  #   --docs               Also ingest .md/.txt files (default: true)"
