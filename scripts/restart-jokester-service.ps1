#Requires -RunAsAdministrator

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'JokesterAdmin'
$firewallRuleName = 'Jokester Admin API TCP 5049'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$publish = [IO.Path]::GetFullPath((Join-Path $root 'publish'))
$staging = [IO.Path]::GetFullPath((Join-Path $root 'publish-next'))
$logs = [IO.Path]::GetFullPath((Join-Path $root 'logs'))
$nssm = 'D:\Tools\nssm-2.24\win64\nssm.exe'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = [IO.Path]::GetFullPath((Join-Path $root "publish-backup-$timestamp"))
$failed = [IO.Path]::GetFullPath((Join-Path $root "publish-failed-$timestamp"))
$statusFile = Join-Path $logs 'service-deploy-status.json'

foreach ($path in @($publish, $staging, $logs, $backup, $failed)) {
    if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe deployment path: $path"
    }
}
if (!(Test-Path -LiteralPath $staging -PathType Container)) {
    throw "Staged publish directory does not exist: $staging"
}
if (!(Test-Path -LiteralPath (Join-Path $staging 'jokester.admin.dll') -PathType Leaf)) {
    throw 'Staged publish output is incomplete.'
}
if (!(Test-Path -LiteralPath $nssm -PathType Leaf) -or !(Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    throw 'NSSM or dotnet executable is missing.'
}

New-Item -ItemType Directory -Path $logs -Force | Out-Null

try {
    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    $listeners = Get-NetTCPConnection -State Listen -LocalPort 5049 -ErrorAction SilentlyContinue
    foreach ($listener in $listeners) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($listener.OwningProcess)"
        $expectedDll = Join-Path $publish 'jokester.admin.dll'
        if ($process.CommandLine -notlike "*$expectedDll*") {
            throw "Port 5049 is owned by an unexpected process: $($process.ProcessId) $($process.CommandLine)"
        }
        Stop-Process -Id $process.ProcessId -Force
    }

    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-NetTCPConnection -State Listen -LocalPort 5049 -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-NetTCPConnection -State Listen -LocalPort 5049 -ErrorAction SilentlyContinue) {
        throw 'Port 5049 did not become available.'
    }

    if (Test-Path -LiteralPath $publish) {
        Move-Item -LiteralPath $publish -Destination $backup
    }
    Move-Item -LiteralPath $staging -Destination $publish

    & sc.exe config $serviceName binPath= "`"$nssm`"" start= auto | Out-Null
    & $nssm set $serviceName Application $dotnet | Out-Null
    & $nssm set $serviceName AppDirectory $root | Out-Null
    & $nssm set $serviceName AppParameters (Join-Path $publish 'jokester.admin.dll') | Out-Null
    & $nssm set $serviceName AppStdout (Join-Path $logs 'jokester-admin.out.log') | Out-Null
    & $nssm set $serviceName AppStderr (Join-Path $logs 'jokester-admin.err.log') | Out-Null
    & $nssm set $serviceName AppRotateFiles 1 | Out-Null
    & $nssm set $serviceName AppRotateOnline 1 | Out-Null
    & $nssm set $serviceName AppRotateBytes 10485760 | Out-Null
    & $nssm set $serviceName AppExit Default Restart | Out-Null
    & $nssm set $serviceName AppRestartDelay 5000 | Out-Null
    & sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

    $firewallRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
    if ($null -eq $firewallRule) {
        New-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Action Allow -Enabled True -Profile Any -Protocol TCP -LocalPort 5049 | Out-Null
    } else {
        $firewallRule | Set-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -Profile Any | Out-Null
    }

    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))

    $healthy = $false
    $deadline = (Get-Date).AddSeconds(60)
    while (!$healthy -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5049/swagger/index.html' -TimeoutSec 5
            $healthy = $response.StatusCode -eq 200
        } catch {
            $healthy = $false
        }
    }
    if (!$healthy) {
        throw 'JokesterAdmin service started but did not pass the local HTTP health check.'
    }

    [pscustomobject]@{
        success = $true
        service = $serviceName
        serviceStatus = (Get-Service -Name $serviceName).Status.ToString()
        localUrl = 'http://127.0.0.1:5049'
        listenUrl = 'http://0.0.0.0:5049'
        backupDirectory = $backup
        completedAt = (Get-Date).ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $statusFile -Encoding UTF8
} catch {
    $failureMessage = $_.Exception.Message
    try { Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue } catch {}
    if ((Test-Path -LiteralPath $publish) -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $publish -Destination $failed
        Move-Item -LiteralPath $backup -Destination $publish
        try { Start-Service -Name $serviceName } catch {}
    }
    [pscustomobject]@{
        success = $false
        service = $serviceName
        error = $failureMessage
        failedAt = (Get-Date).ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath $statusFile -Encoding UTF8
    throw
}
