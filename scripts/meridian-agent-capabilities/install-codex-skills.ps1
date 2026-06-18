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
    return (Join-Path $HOME '.codex\skills')
}

function Get-DefaultSourceRoot {
    $scriptPath = $PSScriptRoot

    if (-not [string]::IsNullOrWhiteSpace($scriptPath)) {
        $localSkills = Join-Path $scriptPath 'skills'

        if (Test-Path -LiteralPath $localSkills -PathType Container) {
            return $localSkills
        }
    }

    $repositoryRoot = Get-RepositoryRoot
    return (Join-Path $repositoryRoot 'docs\agent-capabilities\skills')
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

function Read-InstallTargetSelection {
    while ($true) {
        $choice = Read-Host "Install skills to repo .agents\skills? [y/N]"

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

function Read-SkillMetadata {
    param(
        [Parameter(Mandatory)]
        [string]$SkillPath
    )

    $skillFile = Join-Path $SkillPath 'SKILL.md'

    if (-not (Test-Path -LiteralPath $skillFile -PathType Leaf)) {
        throw "Missing SKILL.md in '$SkillPath'."
    }

    $lines = Get-Content -LiteralPath $skillFile -Encoding UTF8

    if ($lines.Count -lt 4 -or $lines[0] -ne '---') {
        throw "Invalid frontmatter in '$skillFile': first line must be '---'."
    }

    $closingIndex = -1

    for ($index = 1; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -eq '---') {
            $closingIndex = $index
            break
        }
    }

    if ($closingIndex -lt 0) {
        throw "Invalid frontmatter in '$skillFile': missing closing '---'."
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
            throw "Invalid frontmatter in '$skillFile': missing '$requiredKey'."
        }
    }

    if ($metadata['name'] -notmatch '^[a-z0-9][a-z0-9-]{0,62}$') {
        throw "Invalid skill name '$($metadata['name'])' in '$skillFile'. Use lowercase letters, digits, and hyphens only."
    }

    $folderName = Split-Path -Path $SkillPath -Leaf

    if ($metadata['name'] -ne $folderName) {
        throw "Skill name '$($metadata['name'])' does not match folder name '$folderName'."
    }

    [pscustomobject]@{
        Name = $metadata['name']
        Description = $metadata['description']
        Path = $SkillPath
    }
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
            $DestinationRoot = Join-Path (Get-RepositoryRoot) '.agents\skills'
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
    throw "Source skill directory does not exist: '$resolvedSourceRoot'."
}

$skills = Get-ChildItem -LiteralPath $resolvedSourceRoot -Directory |
    Sort-Object -Property Name |
    ForEach-Object { Read-SkillMetadata -SkillPath $_.FullName }

if ($skills.Count -eq 0) {
    throw "No skill folders found under '$resolvedSourceRoot'."
}

Write-Host "Source:      $resolvedSourceRoot"
Write-Host "Destination: $resolvedDestinationRoot"
Write-Host ''

if ($List) {
    $skills | Format-Table -AutoSize Name, Description, Path
    return
}

New-Item -ItemType Directory -Path $resolvedDestinationRoot -Force | Out-Null

foreach ($skill in $skills) {
    $targetPath = Join-Path $resolvedDestinationRoot $skill.Name

    if ($Clean -and (Test-Path -LiteralPath $targetPath)) {
        Assert-ChildPath -ParentPath $resolvedDestinationRoot -ChildPath $targetPath

        if ($PSCmdlet.ShouldProcess($targetPath, 'Remove existing installed skill')) {
            Remove-Item -LiteralPath $targetPath -Recurse -Force
        }
    }

    if ($PSCmdlet.ShouldProcess($targetPath, "Install skill '$($skill.Name)'")) {
        New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
        Copy-Item -Path (Join-Path $skill.Path '*') -Destination $targetPath -Recurse -Force
        Write-Host "Installed $($skill.Name)"
    }
}
