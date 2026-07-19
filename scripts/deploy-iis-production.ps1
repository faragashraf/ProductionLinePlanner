[CmdletBinding()]
param(
    [string]$RollbackTo,
    [switch]$DryRun,
    [Parameter(Mandatory = $true)]
    [string]$FrontendSiteName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$FrontendPhysicalPath = 'C:\inetpub\wwwroot\app'
$FrontendApplicationPath = '/app'
$BackendDefinition = [pscustomobject]@{
        SiteName = 'DayoubApi'
        AppPool = 'DayoubBackend'
        Port = 9000
        PhysicalPath = 'C:\inetpub\wwwroot\Dayoub\api'
        ArtifactPath = 'backend'
        BackupPath = 'api'
    }
$BackendHealthUrls = @('http://192.168.1.99:9000/api/health', 'http://localhost:9000/api/health')
$FrontendUrls = @('http://dayoub.local/app/')
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

function Get-FrontendDefinition {
    Get-Website -Name $FrontendSiteName -ErrorAction Stop | Out-Null
    $binding = Get-WebBinding -Name $FrontendSiteName -Protocol 'http' | Where-Object {
        $_.bindingInformation -match ':80:'
    }
    if (-not $binding) {
        throw "IIS site '$FrontendSiteName' does not have an HTTP port-80 binding. Run scripts\\inspect-iis-frontend-binding.ps1 before deployment."
    }

    $application = Get-WebApplication -Site $FrontendSiteName -Name $FrontendApplicationPath -ErrorAction Stop
    $appPool = $application.ApplicationPool
    if ([string]::IsNullOrWhiteSpace($appPool)) {
        throw "IIS site '$FrontendSiteName' does not define an application pool."
    }
    Get-Item "IIS:\AppPools\$appPool" -ErrorAction Stop | Out-Null

    $currentPath = [IO.Path]::GetFullPath($application.PhysicalPath)
    $expectedPath = [IO.Path]::GetFullPath($FrontendPhysicalPath)
    if ($currentPath -ne $expectedPath -and -not $RollbackTo) {
        throw "IIS application '$FrontendSiteName$FrontendApplicationPath' has physical path '$currentPath', expected '$expectedPath'. Configure the IIS application before deployment."
    }

    [pscustomobject]@{
        SiteName = $FrontendSiteName
        ApplicationPath = $FrontendApplicationPath
        AppPool = $appPool
        PhysicalPath = $FrontendPhysicalPath
        CurrentPhysicalPath = $currentPath
        ArtifactPath = 'frontend'
        BackupPath = 'app'
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

function Assert-FrontendArtifact {
    $frontendArtifactPath = Join-Path $ArtifactRoot 'frontend'
    $indexPath = Join-Path $frontendArtifactPath 'index.html'
    if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
        throw "Frontend artifact is missing index.html: $indexPath"
    }

    $indexHtml = Get-Content -LiteralPath $indexPath -Raw
    if ($indexHtml -notmatch '<base href="/app/">') {
        throw 'Frontend artifact index.html must contain <base href="/app/">.'
    }
    foreach ($pattern in @('main-*.js', 'polyfills-*.js', 'styles-*.css')) {
        if (-not (Get-ChildItem -LiteralPath $frontendArtifactPath -Filter $pattern -File | Select-Object -First 1)) {
            throw "Frontend artifact is missing an asset matching $pattern."
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $frontendArtifactPath 'assets\brand\manifest.webmanifest') -PathType Leaf)) {
        throw 'Frontend artifact is missing assets/brand/manifest.webmanifest.'
    }
}

function Start-ConfiguredPools([array]$definitions) {
    foreach ($definition in $definitions | Sort-Object { $_.AppPool -eq 'DayoubBackend' } -Descending | Select-Object -Unique AppPool) {
        Start-WebAppPool -Name $definition.AppPool
    }
}

function Stop-ConfiguredPools([array]$definitions) {
    foreach ($definition in $definitions | Sort-Object { $_.AppPool -eq 'DayoubBackend' } | Select-Object -Unique AppPool) {
        Stop-WebAppPool -Name $definition.AppPool
    }
}

function Assert-PoolStates([array]$definitions) {
    foreach ($definition in $definitions | Select-Object -Unique AppPool) {
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

function Invoke-FrontendSmoke {
    $indexResponse = Invoke-WebRequest -Uri 'http://dayoub.local/app/' -UseBasicParsing -TimeoutSec 20
    if ($indexResponse.StatusCode -lt 200 -or $indexResponse.StatusCode -ge 400) {
        throw "Frontend index smoke failed with status $($indexResponse.StatusCode)."
    }
    if ($indexResponse.Content -notmatch '<base href="/app/">') {
        throw 'Deployed frontend index.html does not contain <base href="/app/">.'
    }

    foreach ($pattern in @('main-[^"\s]+\.js', 'polyfills-[^"\s]+\.js', 'styles-[^"\s]+\.css')) {
        $asset = [regex]::Match($indexResponse.Content, $pattern).Value
        if ([string]::IsNullOrWhiteSpace($asset)) { throw "Frontend index.html is missing $pattern." }
        Invoke-HttpSmoke -urls @("http://dayoub.local/app/$asset") -label "Frontend asset $asset"
    }

    Invoke-HttpSmoke -urls @('http://dayoub.local/app/assets/brand/manifest.webmanifest') -label 'Frontend manifest'
    Invoke-HttpSmoke -urls @('http://dayoub.local/app/login') -label 'Frontend deep-route refresh'
}

Assert-Administrator
Import-Module WebAdministration -ErrorAction Stop
$frontendDefinition = Get-FrontendDefinition
Assert-IisSite $BackendDefinition
$SiteDefinitions = @($frontendDefinition, $BackendDefinition)

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
    $rollbackStatePath = Join-Path $requestedBackup 'deployment-state.json'
    if (-not (Test-Path -LiteralPath $rollbackStatePath -PathType Leaf)) {
        throw "Rollback backup is missing deployment-state.json: $requestedBackup"
    }
    $rollbackState = Get-Content -LiteralPath $rollbackStatePath -Raw | ConvertFrom-Json
    if ($rollbackState.FrontendSiteName -ne $FrontendSiteName -or [string]::IsNullOrWhiteSpace($rollbackState.FrontendPreviousPhysicalPath)) {
        throw "Rollback state does not match frontend site '$FrontendSiteName'."
    }
} else {
    Assert-FrontendArtifact
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
    [pscustomobject]@{
        FrontendSiteName = $frontendDefinition.SiteName
        FrontendPreviousPhysicalPath = $frontendDefinition.CurrentPhysicalPath
        FrontendTargetPhysicalPath = $FrontendPhysicalPath
        FrontendApplicationPath = $FrontendApplicationPath
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupRoot 'deployment-state.json') -Encoding UTF8
    foreach ($definition in $SiteDefinitions) {
        $backupPath = Join-Path $backupRoot $definition.BackupPath
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        $backupSource = if ($definition.SiteName -eq $frontendDefinition.SiteName) { $frontendDefinition.CurrentPhysicalPath } else { $definition.PhysicalPath }
        Invoke-Robocopy -source $backupSource -destination $backupPath
    }

    Stop-ConfiguredPools $SiteDefinitions
    Set-Content -LiteralPath $backendOfflineFile -Value 'Dayoub API deployment in progress.' -Encoding UTF8

    if ($RollbackTo) {
        Set-ItemProperty "IIS:\Sites\$FrontendSiteName$FrontendApplicationPath" -Name physicalPath -Value $rollbackState.FrontendPreviousPhysicalPath
    }

    foreach ($definition in $SiteDefinitions) {
        $sourcePath = if ($RollbackTo) { Join-Path $sourceRoot $definition.BackupPath } else { Join-Path $sourceRoot $definition.ArtifactPath }
        $destinationPath = if ($RollbackTo -and $definition.SiteName -eq $frontendDefinition.SiteName) { $rollbackState.FrontendPreviousPhysicalPath } else { $definition.PhysicalPath }
        Invoke-Robocopy -source $sourcePath -destination $destinationPath -Mirror -PreserveLocalConfig
    }

    Remove-Item -LiteralPath $backendOfflineFile -Force -ErrorAction SilentlyContinue
    Start-ConfiguredPools $SiteDefinitions
    Assert-PoolStates $SiteDefinitions
    Invoke-HttpSmoke -urls $BackendHealthUrls -label 'Backend health'
    Invoke-HttpSmoke -urls $FrontendUrls -label 'Frontend'
    Invoke-FrontendSmoke
    Write-Host "IIS deployment succeeded. Backup: $backupRoot"
} catch {
    Remove-Item -LiteralPath $backendOfflineFile -Force -ErrorAction SilentlyContinue
    try { Start-ConfiguredPools $SiteDefinitions } catch { Write-Warning "Could not restart one or more pools: $($_.Exception.Message)" }
    throw "IIS deployment failed. Existing content backup: $backupRoot. Roll back with: .\scripts\deploy-iis-production.ps1 -FrontendSiteName '$FrontendSiteName' -RollbackTo '$backupRoot'. Root cause: $($_.Exception.Message)"
}
