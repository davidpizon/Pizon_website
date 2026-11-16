<#
.SYNOPSIS
    Copies the newest nightly database backup from the VPS to this PC.

.PARAMETER Server
    SSH target, e.g. bbs@203.0.113.10. Defaults to $env:PIZON_SERVER.

.PARAMETER Destination
    Local folder (default: src/deploy/backups, which is git-ignored).
#>
[CmdletBinding()]
param(
    [string] $Server = $env:PIZON_SERVER,
    [string] $Destination = (Join-Path $PSScriptRoot 'backups')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $Server) {
    throw 'Set -Server bbs@<vps-ip> or $env:PIZON_SERVER.'
}

$latest = (ssh $Server 'ls -1t ~/pizon/backups/bbs-*.db 2>/dev/null | head -n 1').Trim()
if (-not $latest) {
    throw 'No backups found on the server yet (the first one runs overnight).'
}

New-Item -ItemType Directory -Force $Destination | Out-Null
scp "${Server}:$latest" $Destination
if ($LASTEXITCODE -ne 0) {
    throw 'scp failed.'
}
Write-Host "Saved $(Join-Path $Destination (Split-Path $latest -Leaf))" -ForegroundColor Green
