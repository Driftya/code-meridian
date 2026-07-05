param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-XmlAttributeInt {
    param(
        [Parameter(Mandatory = $true)]
        [System.Xml.XmlElement]$Element,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $value = $Element.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return 0
    }

    return [int]$value
}

$total = 0
$passed = 0
$failed = 0
$skipped = 0

$trxFiles = Get-ChildItem -Path $ResultsRoot -Recurse -Filter *.trx -File -ErrorAction SilentlyContinue
foreach ($file in $trxFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName
    $counters = $xml.TestRun.ResultSummary.Counters
    if ($null -eq $counters) {
        continue
    }

    $total += Get-XmlAttributeInt -Element $counters -Name total
    $passed += Get-XmlAttributeInt -Element $counters -Name passed
    $failed += Get-XmlAttributeInt -Element $counters -Name failed
    $failed += Get-XmlAttributeInt -Element $counters -Name error
    $failed += Get-XmlAttributeInt -Element $counters -Name timeout
    $failed += Get-XmlAttributeInt -Element $counters -Name aborted
    $failed += Get-XmlAttributeInt -Element $counters -Name inconclusive
    $skipped += Get-XmlAttributeInt -Element $counters -Name notExecuted
}

$junitFiles = Get-ChildItem -Path $ResultsRoot -Recurse -Filter *.xml -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*.junit.xml' }

foreach ($file in $junitFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName
    $root = $xml.DocumentElement
    if ($null -eq $root) {
        continue
    }

    if ($root.Name -eq 'testsuites') {
        $total += Get-XmlAttributeInt -Element $root -Name tests
        $failedCount = (Get-XmlAttributeInt -Element $root -Name failures) + (Get-XmlAttributeInt -Element $root -Name errors)
        $skippedCount = Get-XmlAttributeInt -Element $root -Name skipped
    }
    elseif ($root.Name -eq 'testsuite') {
        $total += Get-XmlAttributeInt -Element $root -Name tests
        $failedCount = (Get-XmlAttributeInt -Element $root -Name failures) + (Get-XmlAttributeInt -Element $root -Name errors)
        $skippedCount = Get-XmlAttributeInt -Element $root -Name skipped
    }
    else {
        continue
    }

    $failed += $failedCount
    $skipped += $skippedCount
    $passed += [Math]::Max(0, (Get-XmlAttributeInt -Element $root -Name tests) - $failedCount - $skippedCount)
}

if ($total -le 0) {
    throw "No test results were found under '$ResultsRoot'."
}

$color = if ($failed -gt 0) {
    'red'
}
elseif ($skipped -gt 0) {
    'yellow'
}
else {
    'brightgreen'
}

$message = "$passed/$total passed"
if ($skipped -gt 0) {
    $message += " ($skipped skipped)"
}

$badge = [ordered]@{
    schemaVersion = 1
    label = 'tests'
    message = $message
    color = $color
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$badge | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath
