[CmdletBinding()]
param(
    [string]$RollbackTo,
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SiteDefinitions = @(
    [pscustomobject]@{
        SiteName = 'Dayoub'
        AppPool = 'Dayoub'
        Port = 8000
        PhysicalPath = 'C:\inetpub\wwwroot\Dayoub\app'
        ArtifactPath = 'frontend'
        BackupPath = 'app'
    },
    [pscustomobject]@{
        SiteName = 'DayoubApi'
        AppPool = 'DayoubBackend'
        Port = 9000
        PhysicalPath = 'C:\inetpub\wwwroot\Dayoub\api'
        ArtifactPath = 'backend'
        BackupPath = 'api'
    }
)
$BackendHealthUrls = @('http://192.168.1.99:9000/api/health', 'http://localhost:9000/api/health')
$FrontendUrls = @('http://192.168.1.99:8000/', 'http://localhost:8000/')
$BackupBasePath = 'C:\inetpub\wwwroot\Dayoub\backups'
$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$ArtifactRoot = Join-Path $RepositoryRoot 'artifacts\iis'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated PowerShell session (Administrator).'
    }
}

function Assert-IisSite([pscustomobject]$definition) {
    $site = Get-Website -Name $definition.SiteName -ErrorAction Stop
    if ([IO.Path]::GetFullPath($site.physicalPath) -ne [IO.Path]::GetFullPath($definition.PhysicalPath)) {
        throw "IIS site '$($definition.SiteName)' has physical path '$($site.physicalPath)', expected '$($definition.PhysicalPath)'."
    }

    $binding = Get-WebBinding -Name $definition.SiteName -Protocol 'http' | Where-Object {
        $_.bindingInformation -eq "192.168.1.99:$($definition.Port):"
    }
    if (-not $binding) {
        throw "IIS site '$($definition.SiteName)' does not have the required binding 192.168.1.99:$($definition.Port)."
    }

    Get-Item "IIS:\AppPools\$($definition.AppPool)" -ErrorAction Stop | Out-Null
    if (-not (Test-Path -LiteralPath $definition.PhysicalPath -PathType Container)) {
        throw "IIS physical path is missing: $($definition.PhysicalPath)"
    }
}

function Invoke-Robocopy([string]$source, [string]$destination, [switch]$Mirror, [switch]$PreserveLocalConfig) {
    $arguments = @($source, $destination, '/E', '/COPY:DAT', '/DCOPY:DAT', '/R:2', '/W:2', '/NP', '/NFL', '/NDL')
    if ($Mirror) { $arguments += '/MIR' }
    if ($PreserveLocalConfig) { $arguments += @('/XF', 'appsettings.*.local.json') }
    & robocopy @arguments
    if ($LASTEXITCODE -gt 7) {
        throw "Robocopy failed copying '$source' to '$destination' (exit code $LASTEXITCODE)."
    }
}

function Start-ConfiguredPools {
    foreach ($definition in $SiteDefinitions | Sort-Object { $_.AppPool -eq 'DayoubBackend' } -Descending) {
        Start-WebAppPool -Name $definition.AppPool
    }
}

function Stop-ConfiguredPools {
    foreach ($definition in $SiteDefinitions | Sort-Object { $_.AppPool -eq 'DayoubBackend' }) {
        Stop-WebAppPool -Name $definition.AppPool
    }
}

function Assert-PoolStates {
    foreach ($definition in $SiteDefinitions) {
        $state = (Get-WebAppPoolState -Name $definition.AppPool).Value
        if ($state -ne 'Started') { throw "Application pool '$($definition.AppPool)' is '$state', expected Started." }
    }
}

function Invoke-HttpSmoke([string[]]$urls, [string]$label) {
    $failures = @()
    foreach ($url in $urls) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 20
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                Write-Host "$label smoke passed: $url ($($response.StatusCode))"
                return
            }
            $failures += "$url returned $($response.StatusCode)"
        } catch {
            $failures += "$url failed: $($_.Exception.Message)"
        }
    }
    throw "$label smoke failed. $($failures -join '; ')"
}

Assert-Administrator
Import-Module WebAdministration -ErrorAction Stop
foreach ($definition in $SiteDefinitions) { Assert-IisSite $definition }

$sourceRoot = $ArtifactRoot
if ($RollbackTo) {
    $requestedBackup = [IO.Path]::GetFullPath($RollbackTo)
    $approvedBackupBase = [IO.Path]::GetFullPath($BackupBasePath)
    $approvedBackupPrefix = $approvedBackupBase.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $requestedBackup.StartsWith($approvedBackupPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "RollbackTo must be a folder below $BackupBasePath."
    }
    foreach ($definition in $SiteDefinitions) {
        if (-not (Test-Path -LiteralPath (Join-Path $requestedBackup $definition.BackupPath) -PathType Container)) {
            throw "Rollback backup is missing $($definition.BackupPath): $requestedBackup"
        }
    }
    $sourceRoot = $requestedBackup
} else {
    foreach ($definition in $SiteDefinitions) {
        $artifactPath = Join-Path $ArtifactRoot $definition.ArtifactPath
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Container)) {
            throw "Missing IIS artifact: $artifactPath. Run npm run build:prod:iis from src/frontend first."
        }
    }
}

if ($DryRun) {
    Write-Host "Dry run succeeded. Source: $sourceRoot"
    foreach ($definition in $SiteDefinitions) {
        Write-Host "Would mirror $($definition.ArtifactPath) to $($definition.PhysicalPath) after backup."
    }
    exit 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = Join-Path $BackupBasePath $timestamp
$backendOfflineFile = Join-Path $SiteDefinitions[1].PhysicalPath 'app_offline.htm'

try {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    foreach ($definition in $SiteDefinitions) {
        $backupPath = Join-Path $backupRoot $definition.BackupPath
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        Invoke-Robocopy -source $definition.PhysicalPath -destination $backupPath
    }

    Stop-ConfiguredPools
    Set-Content -LiteralPath $backendOfflineFile -Value 'Dayoub API deployment in progress.' -Encoding UTF8

    foreach ($definition in $SiteDefinitions) {
        $sourcePath = if ($RollbackTo) { Join-Path $sourceRoot $definition.BackupPath } else { Join-Path $sourceRoot $definition.ArtifactPath }
        Invoke-Robocopy -source $sourcePath -destination $definition.PhysicalPath -Mirror -PreserveLocalConfig
    }

    Remove-Item -LiteralPath $backendOfflineFile -Force -ErrorAction SilentlyContinue
    Start-ConfiguredPools
    Assert-PoolStates
    Invoke-HttpSmoke -urls $BackendHealthUrls -label 'Backend health'
    Invoke-HttpSmoke -urls $FrontendUrls -label 'Frontend'
    Write-Host "IIS deployment succeeded. Backup: $backupRoot"
} catch {
    Remove-Item -LiteralPath $backendOfflineFile -Force -ErrorAction SilentlyContinue
    try { Start-ConfiguredPools } catch { Write-Warning "Could not restart one or more pools: $($_.Exception.Message)" }
    throw "IIS deployment failed. Existing content backup: $backupRoot. Roll back with: .\scripts\deploy-iis-production.ps1 -RollbackTo '$backupRoot'. Root cause: $($_.Exception.Message)"
}
