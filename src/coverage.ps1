<#
.SYNOPSIS
    The single test + coverage gate for PIZON BBS.

.DESCRIPTION
    1. Builds the solution with every warning treated as an error.
    2. Runs every .NET test project with coverlet (coverlet.collector, settings in tests/coverage.runsettings).
    3. Merges the per-project Cobertura files with ReportGenerator and fails if merged line or branch
       coverage is under the threshold (each test project only sees part of the code, so per-project
       thresholds would fail falsely).
    4. Runs the JavaScript tests (node --test) with built-in coverage and the same thresholds for lines,
       branches and functions, plus the JSDoc / no-var / unused-export conventions.

    The HTML report is written to src/TestResults/coverage/ (git-ignored). Exits non-zero on any failure.

    Exclusions (see tests/coverage.runsettings): generated code, EF Core migrations, the Aspire AppHost
    (covered by its smoke test) and the dotnet-new door template. Nothing else is excluded without a
    written reason in the code.

.PARAMETER Threshold
    Minimum coverage percentage (default 80).

.PARAMETER SkipAppHost
    Skips the Aspire AppHost smoke test (it starts the whole app and takes about 30 seconds).
#>
[CmdletBinding()]
param(
    [double] $Threshold = 80,
    [switch] $SkipAppHost
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$src = $PSScriptRoot
$results = Join-Path $src 'TestResults'
$raw = Join-Path $results 'raw'
$report = Join-Path $results 'coverage'
$solution = Join-Path $src 'Pizon.slnx'

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed (exit code $LASTEXITCODE)."
    }
}

if (Test-Path $raw) { Remove-Item $raw -Recurse -Force }
if (Test-Path $report) { Remove-Item $report -Recurse -Force }

Invoke-Step 'Build (warnings as errors)' {
    dotnet build $solution -c Debug -warnaserror -nologo -v:minimal
}

$filter = if ($SkipAppHost) { 'FullyQualifiedName!~Pizon.AppHost.Tests' } else { $null }
Invoke-Step '.NET tests with coverlet' {
    $testArgs = @('test', $solution, '--no-build', '-nologo',
        '--settings', (Join-Path $src 'tests/coverage.runsettings'),
        '--collect', 'XPlat Code Coverage',
        '--results-directory', $raw)
    if ($filter) { $testArgs += @('--filter', $filter) }
    dotnet @testArgs
}

# Prefer the pinned local tool (src/.config/dotnet-tools.json); fall back to a global install.
$reportGenerator = @('reportgenerator')
Push-Location $src
try {
    dotnet tool run reportgenerator -- -h *> $null
    if ($LASTEXITCODE -eq 0) { $reportGenerator = @('dotnet', 'tool', 'run', 'reportgenerator', '--') }
}
finally { Pop-Location }
if ($reportGenerator.Count -eq 1 -and -not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    throw 'ReportGenerator not found. Run "dotnet tool restore" in src/.'
}

Invoke-Step 'Merge coverage (ReportGenerator)' {
    $exe = $reportGenerator[0]
    $rest = @($reportGenerator | Select-Object -Skip 1)
    & $exe @rest "-reports:$raw/**/coverage.cobertura.xml" "-targetdir:$report" '-reporttypes:Html;Cobertura;TextSummary' '-verbosity:Warning'
}

$merged = [xml](Get-Content (Join-Path $report 'Cobertura.xml') -Raw)
$line = [double]::Parse($merged.DocumentElement.GetAttribute('line-rate'), [Globalization.CultureInfo]::InvariantCulture) * 100
$branch = [double]::Parse($merged.DocumentElement.GetAttribute('branch-rate'), [Globalization.CultureInfo]::InvariantCulture) * 100

Write-Host '==> JavaScript tests with coverage' -ForegroundColor Cyan
Push-Location $src
try {
    $jsTests = Get-ChildItem (Join-Path $src 'tests/js') -Filter '*.test.mjs' | ForEach-Object { $_.FullName }
    $jsOutput = node --test --experimental-test-coverage `
        "--test-coverage-lines=$Threshold" "--test-coverage-branches=$Threshold" "--test-coverage-functions=$Threshold" `
        '--test-coverage-include=Pizon.Web.Client/wwwroot/js/**' '--test-coverage-include=tools/sh-to-anim.mjs' `
        --throw-deprecation @jsTests 2>&1
    $jsExit = $LASTEXITCODE
}
finally { Pop-Location }
$jsOutput | Out-File (Join-Path $results 'js-coverage.txt') -Encoding utf8
$jsSummary = $jsOutput | Where-Object { $_ -match 'all files\s*\|' } | Select-Object -Last 1
if ($jsExit -ne 0) {
    $jsOutput | Write-Host
    throw "JavaScript tests or coverage failed (exit code $jsExit)."
}
$js = if ($jsSummary -match 'all files\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)\s*\|\s*([\d.]+)') {
    "lines $($Matches[1])% branches $($Matches[2])% functions $($Matches[3])%"
} else { 'passed' }

$summary = '.NET line {0:N1}% branch {1:N1}% | JS {2}' -f $line, $branch, $js
if ($line -lt $Threshold -or $branch -lt $Threshold) {
    Write-Host "$summary  FAIL (.NET under $Threshold%)" -ForegroundColor Red
    Write-Host "Report: $(Join-Path $report 'index.html')"
    exit 1
}

Write-Host "$summary  OK" -ForegroundColor Green
Write-Host "Report: $(Join-Path $report 'index.html')"
