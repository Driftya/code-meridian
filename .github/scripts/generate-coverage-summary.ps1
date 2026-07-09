param(
    [Parameter(Mandatory = $true)]
    [string]$CoverageRoot,

    [Parameter(Mandatory = $true)]
    [string]$BadgeOutputRoot,

    [Parameter(Mandatory = $true)]
    [string]$SummaryOutputPath,

    [string]$DotNetSummaryJsonPath,

    [string]$TypeScriptSummaryJsonPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InvariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Get-CoberturaAttributeInt64 {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Element,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $value = $Element.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return [int64]0
    }

    return [int64]$value
}

function Get-CoverageColor {
    param(
        [Parameter(Mandatory = $true)]
        [double]$Percent
    )

    if ($Percent -ge 90) { return 'brightgreen' }
    if ($Percent -ge 80) { return 'green' }
    if ($Percent -ge 70) { return 'yellowgreen' }
    if ($Percent -ge 60) { return 'yellow' }
    return 'red'
}

function Get-CoverageMetrics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string[]]$Paths
    )

    $covered = [int64]0
    $valid = [int64]0

    foreach ($path in $Paths) {
        [xml]$xml = Get-Content -LiteralPath $path
        $root = $xml.DocumentElement
        if ($null -eq $root -or $root.Name -ne 'coverage') {
            continue
        }

        $covered += Get-CoberturaAttributeInt64 -Element $root -Name 'lines-covered'
        $valid += Get-CoberturaAttributeInt64 -Element $root -Name 'lines-valid'
    }

    if ($valid -le 0) {
        throw "No valid coverage totals were found for '$Label'."
    }

    $percent = [Math]::Round(($covered / $valid) * 100, 2)

    return [pscustomobject]@{
        Label = $Label
        CoveredLines = $covered
        TotalLines = $valid
        Percent = $percent
    }
}

function Get-ReportGeneratorMetrics {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string]$SummaryJsonPath
    )

    if (-not (Test-Path -LiteralPath $SummaryJsonPath)) {
        throw "ReportGenerator summary was not found for '$Label' at '$SummaryJsonPath'."
    }

    $json = Get-Content -LiteralPath $SummaryJsonPath -Raw | ConvertFrom-Json
    if ($null -eq $json.summary) {
        throw "ReportGenerator summary for '$Label' is missing the summary payload."
    }

    $covered = [int64]$json.summary.coveredlines
    $valid = [int64]$json.summary.coverablelines
    if ($valid -le 0) {
        throw "ReportGenerator summary for '$Label' did not contain valid coverable lines."
    }

    return [pscustomobject]@{
        Label = $Label
        CoveredLines = $covered
        TotalLines = $valid
        Percent = [Math]::Round(($covered / $valid) * 100, 2)
    }
}

function Format-Percent {
    param(
        [Parameter(Mandatory = $true)]
        [double]$Percent
    )

    return $Percent.ToString('0.00', $InvariantCulture)
}

function Write-BadgeJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [double]$Percent
    )

    $badge = [ordered]@{
        schemaVersion = 1
        label = $Label
        message = "$(Format-Percent -Percent $Percent)%"
        color = Get-CoverageColor -Percent $Percent
    }

    $directory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $badge | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath
}

$dotnetCoverageFiles = @(Get-ChildItem -Path $CoverageRoot -Recurse -Filter 'coverage.cobertura.xml' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
$typescriptCoverageFiles = @(Get-ChildItem -Path $CoverageRoot -Recurse -Filter 'cobertura-coverage.xml' -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)

if ($dotnetCoverageFiles.Count -eq 0) {
    throw "No .NET coverage reports were found under '$CoverageRoot'."
}

if ($typescriptCoverageFiles.Count -eq 0) {
    throw "No TypeScript coverage reports were found under '$CoverageRoot'."
}

$workspaceRows = @()
foreach ($file in $typescriptCoverageFiles) {
    $workspaceDirectory = Split-Path -Parent (Split-Path -Parent $file)
    $workspaceLabel = Split-Path -Leaf $workspaceDirectory
    $workspaceRows += Get-CoverageMetrics -Label $workspaceLabel -Paths @($file)
}

$dotnetMetrics = if (-not [string]::IsNullOrWhiteSpace($DotNetSummaryJsonPath) -and (Test-Path -LiteralPath $DotNetSummaryJsonPath)) {
    Get-ReportGeneratorMetrics -Label '.NET' -SummaryJsonPath $DotNetSummaryJsonPath
} else {
    Get-CoverageMetrics -Label '.NET' -Paths $dotnetCoverageFiles
}

$typescriptMetrics = if (-not [string]::IsNullOrWhiteSpace($TypeScriptSummaryJsonPath) -and (Test-Path -LiteralPath $TypeScriptSummaryJsonPath)) {
    Get-ReportGeneratorMetrics -Label 'TypeScript' -SummaryJsonPath $TypeScriptSummaryJsonPath
} else {
    Get-CoverageMetrics -Label 'TypeScript' -Paths $typescriptCoverageFiles
}

$combinedMetrics = [pscustomobject]@{
    Label = 'Combined'
    CoveredLines = $dotnetMetrics.CoveredLines + $typescriptMetrics.CoveredLines
    TotalLines = $dotnetMetrics.TotalLines + $typescriptMetrics.TotalLines
    Percent = [Math]::Round((($dotnetMetrics.CoveredLines + $typescriptMetrics.CoveredLines) / ($dotnetMetrics.TotalLines + $typescriptMetrics.TotalLines)) * 100, 2)
}

$summaryLines = @(
    '## Coverage Report',
    '',
    '| Suite | Covered lines | Total lines | Line coverage |',
    '| --- | ---: | ---: | ---: |',
    ('| {0} | {1} | {2} | {3}% |' -f $dotnetMetrics.Label, $dotnetMetrics.CoveredLines, $dotnetMetrics.TotalLines, (Format-Percent -Percent $dotnetMetrics.Percent)),
    ('| {0} | {1} | {2} | {3}% |' -f $typescriptMetrics.Label, $typescriptMetrics.CoveredLines, $typescriptMetrics.TotalLines, (Format-Percent -Percent $typescriptMetrics.Percent)),
    ('| {0} | {1} | {2} | {3}% |' -f $combinedMetrics.Label, $combinedMetrics.CoveredLines, $combinedMetrics.TotalLines, (Format-Percent -Percent $combinedMetrics.Percent)),
    '',
    '### TypeScript workspaces',
    '',
    '| Workspace | Covered lines | Total lines | Line coverage |',
    '| --- | ---: | ---: | ---: |'
)

foreach ($row in $workspaceRows | Sort-Object Label) {
    $summaryLines += ('| {0} | {1} | {2} | {3}% |' -f $row.Label, $row.CoveredLines, $row.TotalLines, (Format-Percent -Percent $row.Percent))
}

$summaryDirectory = Split-Path -Parent $SummaryOutputPath
if (-not [string]::IsNullOrWhiteSpace($summaryDirectory)) {
    New-Item -ItemType Directory -Path $summaryDirectory -Force | Out-Null
}

$summaryLines | Set-Content -LiteralPath $SummaryOutputPath

Write-BadgeJson -OutputPath (Join-Path $BadgeOutputRoot 'badges/dotnet-coverage.json') -Label '.NET coverage' -Percent $dotnetMetrics.Percent
Write-BadgeJson -OutputPath (Join-Path $BadgeOutputRoot 'badges/typescript-coverage.json') -Label 'TS coverage' -Percent $typescriptMetrics.Percent
Write-BadgeJson -OutputPath (Join-Path $BadgeOutputRoot 'badges/coverage.json') -Label 'coverage' -Percent $combinedMetrics.Percent
