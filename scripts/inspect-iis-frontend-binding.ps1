[CmdletBinding()]
param(
    [string]$FrontendSiteName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module WebAdministration -ErrorAction Stop

Get-Website |
    Select-Object Name, State, PhysicalPath, ApplicationPool,
        @{Name = 'Bindings'; Expression = {
            ($_.Bindings.Collection |
                ForEach-Object { "$($_.protocol) $($_.bindingInformation)" }) -join ', '
        }}

Get-WebApplication |
    Select-Object Site, Path, PhysicalPath, ApplicationPool

Write-Host ''
Write-Host 'HTTP bindings for 192.168.1.99:80:'
$port80Owners = Get-Website | Where-Object {
    $_.Bindings.Collection | Where-Object {
        $_.protocol -eq 'http' -and $_.bindingInformation -eq '192.168.1.99:80:'
    }
}

if ($port80Owners) {
    $port80Owners | Select-Object Name, State, PhysicalPath, ApplicationPool
} else {
    Write-Host 'No IIS site owns 192.168.1.99:80.'
}

if ($FrontendSiteName) {
    Write-Host ''
    Write-Host "Bindings for frontend site '$FrontendSiteName':"
    Get-WebBinding -Name $FrontendSiteName -Protocol http |
        Select-Object protocol, bindingInformation
}
