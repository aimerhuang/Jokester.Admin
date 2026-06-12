param(
    [string]$BaseUrl = "http://localhost:5049"
)

$uri = "$BaseUrl/api/dev/bootstrap/super-admin"
Invoke-RestMethod -Method Post -Uri $uri
