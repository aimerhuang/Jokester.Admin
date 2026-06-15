# Fix IIS Reverse Proxy 413 Request Entity Too Large
# Run as Administrator

param(
    [string]$SiteName = "ai.jokester.cc",
    [int]$MaxUploadSizeMB = 100
)

$maxBytes = $MaxUploadSizeMB * 1024 * 1024
$ErrorActionPreference = "Stop"

Write-Host "=== Fix IIS Reverse Proxy 413 Error ===" -ForegroundColor Cyan
Write-Host "Target Site: $SiteName"
Write-Host "Max Upload Size: $MaxUploadSizeMB MB ($maxBytes bytes)"
Write-Host ""

try {
    $site = Get-IISSite -Name $SiteName -ErrorAction Stop
} catch {
    Write-Host "ERROR: IIS site '$SiteName' not found. Please check the site name." -ForegroundColor Red
    Write-Host "Available sites:"
    Get-IISSite | ForEach-Object { Write-Host "  - $($_.Name)" }
    exit 1
}

$sitePath = "IIS:\Sites\$SiteName"

# Step 1: Unlock serverRuntime section at global level (it is locked by default)
Write-Host "[1/4] Unlocking serverRuntime section globally..." -ForegroundColor Yellow
try {
    & "$env:windir\system32\inetsrv\appcmd.exe" unlock config -section:system.webServer/serverRuntime
    Write-Host "  OK: serverRuntime section unlocked" -ForegroundColor Green
} catch {
    Write-Host "  WARN: Could not unlock serverRuntime (may already be unlocked): $_" -ForegroundColor DarkYellow
}

# Step 2: Set uploadReadAheadSize (must be set at apphost level due to locking)
Write-Host "[2/4] Setting uploadReadAheadSize = $maxBytes ..." -ForegroundColor Yellow
try {
    Set-WebConfigurationProperty -Filter "system.webServer/serverRuntime" -Name "uploadReadAheadSize" -Value $maxBytes -PSPath $sitePath
    Write-Host "  OK: uploadReadAheadSize set via site config" -ForegroundColor Green
} catch {
    Write-Host "  Trying appcmd to set globally..." -ForegroundColor DarkYellow
    try {
        & "$env:windir\system32\inetsrv\appcmd.exe" set config -section:system.webServer/serverRuntime /uploadReadAheadSize:$maxBytes /commit:apphost
        Write-Host "  OK: uploadReadAheadSize set globally" -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: Failed to set uploadReadAheadSize: $_" -ForegroundColor Red
        Write-Host "  Manual fix: Open IIS Manager -> Configuration Editor -> system.webServer/serverRuntime -> uploadReadAheadSize = $maxBytes"
    }
}

# Step 3: Set requestFiltering requestLimits
Write-Host "[3/4] Setting maxAllowedContentLength = $maxBytes ..." -ForegroundColor Yellow
try {
    Set-WebConfigurationProperty -Filter "system.webServer/security/requestFiltering/requestLimits" -Name "maxAllowedContentLength" -Value $maxBytes -PSPath $sitePath
    Write-Host "  OK: maxAllowedContentLength set" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: Failed to set maxAllowedContentLength: $_" -ForegroundColor Red
}

# Step 4: Set ASP.NET httpRuntime maxRequestLength (for legacy, may not apply to .NET Core)
Write-Host "[4/4] Setting httpRuntime maxRequestLength ..." -ForegroundColor Yellow
try {
    $maxRequestLengthKB = $MaxUploadSizeMB * 1024
    Set-WebConfigurationProperty -Filter "system.web/httpRuntime" -Name "maxRequestLength" -Value $maxRequestLengthKB -PSPath $sitePath
    Write-Host "  OK: maxRequestLength set to ${maxRequestLengthKB}KB" -ForegroundColor Green
} catch {
    Write-Host "  INFO: httpRuntime.maxRequestLength skipped (not applicable for .NET Core)" -ForegroundColor DarkYellow
}

# Restart the IIS site
Write-Host ""
Write-Host "Restarting site $SiteName ..." -ForegroundColor Yellow
Stop-IISSite -Name $SiteName -Confirm:$false -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Start-IISSite -Name $SiteName
Write-Host "  OK: Site restarted" -ForegroundColor Green

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Rebuild and deploy: dotnet publish jokester.admin\jokester.admin.csproj -c Release -o publish"
Write-Host "  2. Restart the Windows service: Restart-Service JokesterAdmin"
Write-Host "  3. Test upload with a small image (< 1MB)"
Write-Host ""
Write-Host "If upload still fails, check:"
Write-Host "  - Cloudflare/CDN body size limit (Free=100MB, Pro=200MB)"
Write-Host "  - Nginx reload: nginx -s reload"
