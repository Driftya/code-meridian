#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourceRoot,
    [string]$DestinationRoot,
    [switch]$List,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepositoryRoot {
    $scriptPath = $PSScriptRoot

    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        return (Get-Location).ProviderPath
    }

    return (Resolve-Path -LiteralPath (Join-Path $scriptPath '..\..')).ProviderPath
}

function Get-DefaultDestinationRoot {
    return (Join-Path $HOME '.codex\agents')
}

function Read-InstallTargetSelection {
    while ($true) {
        $choice = Read-Host "Install agents to repo .codex\agents? [y/N]"

        if ([string]::IsNullOrWhiteSpace($choice)) {
            return 'User'
        }

        switch ($choice.Trim().ToLowerInvariant()) {
            'y' { return 'Repo' }
            'yes' { return 'Repo' }
            'n' { return 'User' }
            'no' { return 'User' }
        }

        Write-Host "Please answer y or n."
    }
}

function Get-DefaultSourceRoot {
    $scriptPath = $PSScriptRoot

    if (-not [string]::IsNullOrWhiteSpace($scriptPath)) {
        $localAgents = Join-Path $scriptPath 'agents'

        if (Test-Path -LiteralPath $localAgents -PathType Container) {
            return $localAgents
        }
    }

    $repositoryRoot = Get-RepositoryRoot
    return (Join-Path $repositoryRoot 'docs\agent-capabilities\agents')
}

function Get-ResolvedPathOrDefault {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        return (Resolve-Path -LiteralPath $Path).ProviderPath
    }

    $parent = Split-Path -Path $Path -Parent
    $leaf = Split-Path -Path $Path -Leaf

    if ([string]::IsNullOrWhiteSpace($parent)) {
        return $Path
    }

    if (Test-Path -LiteralPath $parent) {
        $resolvedParent = (Resolve-Path -LiteralPath $parent).ProviderPath
        return (Join-Path $resolvedParent $leaf)
    }

    return $Path
}

function Read-AgentDefinition {
    param(
        [Parameter(Mandatory)]
        [string]$AgentFile
    )

    $lines = Get-Content -LiteralPath $AgentFile -Encoding UTF8

    if ($lines.Count -lt 4 -or $lines[0] -ne '---') {
        throw "Invalid frontmatter in '$AgentFile': first line must be '---'."
    }

    $closingIndex = -1

    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -eq '---') {
            $closingIndex = $index
            break
        }
    }

    if ($closingIndex -lt 0) {
        throw "Invalid frontmatter in '$AgentFile': missing closing '---'."
    }

    $metadata = @{}

    for ($index = 1; $index -lt $closingIndex; $index++) {
        $line = $lines[$index]

        if ($line -match '^\s*([^:#]+)\s*:\s*(.*)\s*$') {
            $metadata[$matches[1].Trim()] = $matches[2].Trim()
        }
    }

    foreach ($requiredKey in @('name', 'description')) {
        if (-not $metadata.ContainsKey($requiredKey) -or [string]::IsNullOrWhiteSpace($metadata[$requiredKey])) {
            throw "Invalid frontmatter in '$AgentFile': missing '$requiredKey'."
        }
    }

    if ($metadata['name'] -notmatch '^[a-z0-9][a-z0-9-]{0,62}$') {
        throw "Invalid agent name '$($metadata['name'])' in '$AgentFile'. Use lowercase letters, digits, and hyphens only."
    }

    $bodyLines = @()

    if ($closingIndex + 1 -lt $lines.Count) {
        $bodyLines = $lines[($closingIndex + 1)..($lines.Count - 1)]
    }

    $body = ($bodyLines -join "`n").Trim()

    if ([string]::IsNullOrWhiteSpace($body)) {
        throw "Invalid agent file '$AgentFile': missing instructions after frontmatter."
    }

    [pscustomobject]@{
        Name = $metadata['name']
        Description = $metadata['description']
        Instructions = $body
        Path = $AgentFile
    }
}

function ConvertTo-TomlLiteralString {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    if ($Value.Contains("'''")) {
        throw "Cannot generate TOML literal string because the content contains three single quotes."
    }

    return "'''`n$Value`n'''"
}

function ConvertTo-TomlQuotedString {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function ConvertTo-CodexAgentToml {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Agent
    )

    @"
name = $(ConvertTo-TomlQuotedString -Value $Agent.Name)
description = $(ConvertTo-TomlQuotedString -Value $Agent.Description)
developer_instructions = $(ConvertTo-TomlLiteralString -Value $Agent.Instructions)
"@
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)]
        [string]$ParentPath,
        [Parameter(Mandatory)]
        [string]$ChildPath
    )

    $fullParent = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullChild = [System.IO.Path]::GetFullPath($ChildPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $comparison = [System.StringComparison]::OrdinalIgnoreCase

    if (-not $fullChild.StartsWith($fullParent + [System.IO.Path]::DirectorySeparatorChar, $comparison) -and
        -not $fullChild.StartsWith($fullParent + [System.IO.Path]::AltDirectorySeparatorChar, $comparison)) {
        throw "Refusing to clean '$ChildPath' because it is not under '$ParentPath'."
    }
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Get-DefaultSourceRoot
}

if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    if ([Environment]::UserInteractive) {
        $selection = Read-InstallTargetSelection

        if ($selection -eq 'Repo') {
            $DestinationRoot = Join-Path (Get-RepositoryRoot) '.codex\agents'
        }
        else {
            $DestinationRoot = Get-DefaultDestinationRoot
        }
    }
    else {
        $DestinationRoot = Get-DefaultDestinationRoot
    }
}

$resolvedSourceRoot = Get-ResolvedPathOrDefault -Path $SourceRoot
$resolvedDestinationRoot = Get-ResolvedPathOrDefault -Path $DestinationRoot

if (-not (Test-Path -LiteralPath $resolvedSourceRoot -PathType Container)) {
    throw "Source agent directory does not exist: '$resolvedSourceRoot'."
}

$agents = Get-ChildItem -LiteralPath $resolvedSourceRoot -File -Filter '*.md' |
    Sort-Object -Property Name |
    ForEach-Object { Read-AgentDefinition -AgentFile $_.FullName }

if ($agents.Count -eq 0) {
    throw "No agent markdown files found under '$resolvedSourceRoot'."
}

Write-Host "Source:      $resolvedSourceRoot"
Write-Host "Destination: $resolvedDestinationRoot"
Write-Host ''

if ($List) {
    $agents | Format-Table -AutoSize Name, Description, Path
    return
}

New-Item -ItemType Directory -Path $resolvedDestinationRoot -Force | Out-Null

foreach ($agent in $agents) {
    $targetPath = Join-Path $resolvedDestinationRoot "$($agent.Name).toml"

    if ($Clean -and (Test-Path -LiteralPath $targetPath)) {
        Assert-ChildPath -ParentPath $resolvedDestinationRoot -ChildPath $targetPath

        if ($PSCmdlet.ShouldProcess($targetPath, 'Remove existing installed agent')) {
            Remove-Item -LiteralPath $targetPath -Force
        }
    }

    if ($PSCmdlet.ShouldProcess($targetPath, "Install Codex agent '$($agent.Name)'")) {
        $toml = ConvertTo-CodexAgentToml -Agent $agent
        Set-Content -LiteralPath $targetPath -Value $toml -Encoding UTF8
        Write-Host "Installed $($agent.Name)"
    }
}
