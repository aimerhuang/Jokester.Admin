$nssm = "$env:LOCALAPPDATA\Microsoft\WinGet\Links\nssm.exe"
$serviceName = "JokesterAdmin"
$root = "D:\Project\Jokester.Admin"
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$dll = "D:\Project\Jokester.Admin\publish\jokester.admin.dll"
$logs = Join-Path $root "logs"
$wwwroot = Join-Path $root "wwwroot"
if (-not (Test-Path $logs)) { New-Item -ItemType Directory -Path $logs | Out-Null }
if (-not (Test-Path $wwwroot)) { New-Item -ItemType Directory -Path $wwwroot | Out-Null }
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }
    & $nssm remove $serviceName confirm
}
& $nssm install $serviceName $dotnet $dll
if ($LASTEXITCODE -ne 0) { throw "nssm install failed with exit code $LASTEXITCODE" }
& $nssm set $serviceName AppDirectory $root
& $nssm set $serviceName DisplayName "Jokester Admin API"
& $nssm set $serviceName Description "Jokester.Admin .NET backend API"
& $nssm set $serviceName Start SERVICE_AUTO_START
& $nssm set $serviceName AppStdout "D:\Project\Jokester.Admin\logs\jokester-admin.out.log"
& $nssm set $serviceName AppStderr "D:\Project\Jokester.Admin\logs\jokester-admin.err.log"
& $nssm set $serviceName AppRotateFiles 1
& $nssm set $serviceName AppRotateOnline 1
& $nssm set $serviceName AppRotateBytes 10485760
Start-Service -Name $serviceName
Start-Sleep -Seconds 3
Get-Service -Name $serviceName | Format-List Name,Status,StartType
