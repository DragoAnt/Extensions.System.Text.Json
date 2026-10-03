#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when the merged line or branch coverage of a package is below a threshold.

.DESCRIPTION
    Reads every Cobertura report in a directory (one per test run and target framework, as written by
    `dotnet test --coverage --coverage-output-format cobertura`), merges the hits of each source line across
    them, and compares the result with the thresholds.

.EXAMPLE
    dotnet test --solution DragoAnt.System.Text.Json.slnx -c Release --coverage --coverage-output-format cobertura --results-directory TestResults
    pwsh tools/check-coverage.ps1 -ResultsDirectory TestResults -Package DragoAnt.System.Text.Json.Observer -MinLine 88 -MinBranch 75
#>
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [Parameter(Mandatory)] [string] $Package,
    [Parameter(Mandatory)] [double] $MinLine,
    [Parameter(Mandatory)] [double] $MinBranch
)

$ErrorActionPreference = 'Stop'

$reports = @(Get-ChildItem -Path $ResultsDirectory -Filter '*.cobertura.xml' -Recurse -File)
if ($reports.Count -eq 0) {
    Write-Error "No *.cobertura.xml report under '$ResultsDirectory'."
}

$lines = @{}
$branches = @{}
foreach ($report in $reports) {
    [xml] $xml = Get-Content -LiteralPath $report.FullName -Raw
    foreach ($class in $xml.SelectNodes("//package[@name='$Package']/classes/class")) {
        $file = $class.GetAttribute('filename') -replace '\\', '/'
        foreach ($line in $class.SelectNodes('lines/line')) {
            $key = "$file|$($line.GetAttribute('number'))"
            $covered = [int] $line.GetAttribute('hits') -gt 0
            $lines[$key] = ($lines[$key] -eq $true) -or $covered
            if ($line.GetAttribute('branch') -eq 'True' -and $line.GetAttribute('condition-coverage') -match '\((\d+)/(\d+)\)') {
                $hit = [int] $Matches[1]
                $total = [int] $Matches[2]
                if (-not $branches.ContainsKey($key) -or $branches[$key][0] -lt $hit) {
                    $branches[$key] = @($hit, $total)
                }
            }
        }
    }
}

if ($lines.Count -eq 0) {
    Write-Error "Package '$Package' is in none of the $($reports.Count) reports."
}

$lineRate = 100.0 * @($lines.Values | Where-Object { $_ }).Count / $lines.Count
$branchHit = ($branches.Values | ForEach-Object { $_[0] } | Measure-Object -Sum).Sum
$branchTotal = ($branches.Values | ForEach-Object { $_[1] } | Measure-Object -Sum).Sum
$branchRate = if ($branchTotal -gt 0) { 100.0 * $branchHit / $branchTotal } else { 100.0 }

$summary = [string]::Format([cultureinfo]::InvariantCulture, '{0}: line {1:N1}% (min {2}), branch {3:N1}% (min {4}), {5} reports', $Package, $lineRate, $MinLine, $branchRate, $MinBranch, $reports.Count)
if ($lineRate -lt $MinLine -or $branchRate -lt $MinBranch) {
    Write-Host "FAIL $summary"
    exit 1
}

Write-Host "OK $summary"
