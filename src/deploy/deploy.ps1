<#
.SYNOPSIS
    Builds, tests and deploys PIZON BBS to the VPS.

.DESCRIPTION
    1. Runs src/coverage.ps1 (warnings as errors, all tests, 80% coverage gate); stops if it fails.
    2. Builds the Docker image pizon-bbs:<timestamp> from src/.
    3. Streams it to the server (docker save | gzip | ssh docker load); nothing is pushed to a registry.
    4. Copies docker-compose.yml, Caddyfile and backup.sh to ~/pizon and runs docker compose up -d.
    5. Checks https://pizon.com/healthz.
    The tag that was running before is saved in ~/pizon/previous-tag so -Rollback can switch back.

.PARAMETER Server
    SSH target, e.g. bbs@203.0.113.10. Defaults to $env:PIZON_SERVER.

.PARAMETER Url
    Public site URL for the health check.

.PARAMETER Rollback
    Switches back to the previous image tag (no build, no tests).

.PARAMETER SkipTests
    Skips coverage.ps1. Only for emergencies; the gate is the safety net.
#>
[CmdletBinding()]
param(
    [string] $Server = $env:PIZON_SERVER,
    [string] $Url = 'https://pizon.com',
    [switch] $Rollback,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $Server) {
    throw 'Set -Server bbs@<vps-ip> or $env:PIZON_SERVER.'
}

$deployDir = $PSScriptRoot
$src = Split-Path $deployDir -Parent

function Invoke-Native([string] $Name, [scriptblock] $Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed (exit code $LASTEXITCODE)."
    }
}

function Invoke-Remote([string] $Command) {
    ssh $Server $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Remote command failed: $Command"
    }
}

function Wait-Healthy {
    $health = "$Url/healthz"
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $response = Invoke-WebRequest $health -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host "Healthy: $health" -ForegroundColor Green
                return
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }
    throw "Health check failed: $health. Try deploy.ps1 -Rollback."
}

if ($Rollback) {
    $previous = (ssh $Server 'cat ~/pizon/previous-tag 2>/dev/null').Trim()
    if (-not $previous) {
        throw 'No previous tag recorded on the server.'
    }
    Write-Host "==> Rolling back to $previous" -ForegroundColor Yellow
    Invoke-Remote "cd ~/pizon && cat current-tag > previous-tag.new && echo $previous > current-tag && mv previous-tag.new previous-tag && PIZON_TAG=$previous docker compose up -d web"
    Wait-Healthy
    return
}

if (-not $SkipTests) {
    Invoke-Native 'Coverage gate' { pwsh -NoProfile -File (Join-Path $src 'coverage.ps1') }
}

$tag = Get-Date -Format 'yyyyMMdd-HHmmss'
$image = "pizon-bbs:$tag"
Invoke-Native "docker build $image" { docker build -t $image -f (Join-Path $src 'Dockerfile') $src }

Invoke-Native 'Upload image' {
    # cmd keeps the binary pipe intact (PowerShell pipelines would re-encode it).
    cmd /c "docker save $image | gzip | ssh $Server ""gunzip | docker load"""
}

Invoke-Native 'Copy compose files' {
    scp (Join-Path $deployDir 'docker-compose.yml') (Join-Path $deployDir 'Caddyfile') (Join-Path $deployDir 'backup.sh') "${Server}:pizon/"
}

Invoke-Remote "cd ~/pizon && chmod +x backup.sh && test -f .env && (cat current-tag > previous-tag 2>/dev/null || true) && echo $tag > current-tag && PIZON_TAG=$tag docker compose up -d"
Wait-Healthy

# Keep the current and previous images; prune older pizon-bbs tags.
Invoke-Remote 'cd ~/pizon && keep=" $(cat current-tag) $(cat previous-tag 2>/dev/null) "; for t in $(docker images pizon-bbs --format "{{.Tag}}"); do case "$keep" in *" $t "*) ;; *) docker rmi "pizon-bbs:$t" >/dev/null || true;; esac; done'
Write-Host "Deployed $image" -ForegroundColor Green
