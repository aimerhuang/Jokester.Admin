[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5049",
    [Parameter(Mandatory)]
    [string]$Secret
)

if ([string]::IsNullOrWhiteSpace($Secret)) {
    throw "BootstrapAdmin.Secret is required."
}

$uri = "$BaseUrl/api/dev/bootstrap/super-admin"
Invoke-RestMethod -Method Post -Uri $uri -Headers @{ "X-Bootstrap-Secret" = $Secret }
