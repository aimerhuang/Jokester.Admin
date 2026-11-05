[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('init', 'up', 'down', 'restart', 'ps', 'logs', 'seed-admin', 'legal', 'sql', 'mysql', 'backup', 'cli', 'reset', 'help')]
    [string]$Command = 'help',

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$dockerDirectory = $PSScriptRoot
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$ArgumentList)

    & docker compose @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function New-RandomHex {
    $bytes = New-Object byte[] 32
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

function Copy-Template {
    param(
        [Parameter(Mandatory = $true)][string]$Template,
        [Parameter(Mandatory = $true)][string]$Target
    )

    $targetPath = Join-Path $dockerDirectory $Target
    if (Test-Path -LiteralPath $targetPath) {
        Write-Host "Keep existing $Target"
        return
    }

    $content = [System.IO.File]::ReadAllText((Join-Path $dockerDirectory $Template), $utf8WithoutBom)
    # Every placeholder gets its own random value.
    $content = [regex]::Replace($content, '__RANDOM__', { param($match) New-RandomHex })
    [System.IO.File]::WriteAllText($targetPath, $content, $utf8WithoutBom)
    Write-Host "Created $Target"
}

function Assert-Initialized {
    foreach ($file in @('.env', 'api.env')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dockerDirectory $file))) {
            throw "docker/$file is missing. Run '.\jokester.ps1 init' first."
        }
    }
}

function Read-EnvFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $values = [ordered]@{}
    foreach ($line in [System.IO.File]::ReadAllLines($Path, $utf8WithoutBom)) {
        $text = $line.Trim()
        if ($text.Length -eq 0 -or $text.StartsWith('#')) { continue }
        $index = $text.IndexOf('=')
        if ($index -le 0) { continue }
        $values[$text.Substring(0, $index).Trim()] = $text.Substring($index + 1).Trim()
    }
    return $values
}

function Read-PlainPassword {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    $secure = Read-Host -Prompt $Prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Show-Help {
    @'
Usage: .\jokester.ps1 <command> [arguments]

  init               Create .env / api.env from the templates with random secrets
  up [service...]    Build images and start everything, waiting until healthy
  down               Stop and remove containers (data volumes are kept)
  restart [service]  Restart services
  ps                 Show container status
  logs [service]     Follow logs (default: api)
  seed-admin         Create or reset a super admin (prompts for the password)
  legal              Configure legal documents from docker/legal.env (web, then all)
  sql <file>         Run a SQL file (for example docs/migrations/*.sql) against the database
  mysql              Open a MySQL client
  backup [file]      Export the database to a file (default: backups/jokester-<time>.sql)
  cli <args...>      Run a one-off API command, for example: cli --migrate-ai-media --dry-run
  reset              Remove containers AND all data volumes (asks for confirmation)
'@ | Write-Host
}

Push-Location $dockerDirectory
try {
    switch ($Command) {
        'help' {
            Show-Help
        }
        'init' {
            Copy-Template '.env.example' '.env'
            Copy-Template 'api.env.example' 'api.env'
            Write-Host 'Review docker/.env and docker/api.env, then run: .\jokester.ps1 up'
        }
        'up' {
            Assert-Initialized
            Invoke-Compose (@('up', '--detach', '--build', '--wait') + $Arguments)
            $settings = Read-EnvFile (Join-Path $dockerDirectory '.env')
            Write-Host ''
            Write-Host "Web:     $($settings['PUBLIC_ORIGIN'])"
            if ($settings.Contains('API_HOST_PORT')) {
                Write-Host "Swagger: http://localhost:$($settings['API_HOST_PORT'])/swagger"
            }
            if ($settings.Contains('MAILPIT_UI_PORT')) {
                Write-Host "Mailpit: http://localhost:$($settings['MAILPIT_UI_PORT'])"
            }
        }
        'down' {
            Invoke-Compose @('down')
        }
        'restart' {
            Invoke-Compose (@('restart') + $Arguments)
        }
        'ps' {
            Invoke-Compose @('ps')
        }
        'logs' {
            $services = if ($Arguments.Count -gt 0) { $Arguments } else { @('api') }
            Invoke-Compose (@('logs', '--follow', '--tail', '200') + $services)
        }
        'seed-admin' {
            Assert-Initialized
            $userName = Read-Host -Prompt 'Super admin account (user name or email)'
            if ([string]::IsNullOrWhiteSpace($userName)) { throw 'User name is required.' }
            $password = Read-PlainPassword 'Password'
            if ([string]::IsNullOrWhiteSpace($password)) { throw 'Password is required.' }

            # Pass credentials through the process environment so they never appear in arguments.
            $env:BootstrapAdmin__UserName = $userName.Trim()
            $env:BootstrapAdmin__Password = $password
            try {
                Invoke-Compose @('run', '--rm', '--no-TTY',
                    '--env', 'BootstrapAdmin__UserName',
                    '--env', 'BootstrapAdmin__Password',
                    'api', '--seed-admin')
            }
            finally {
                Remove-Item Env:BootstrapAdmin__UserName, Env:BootstrapAdmin__Password -ErrorAction SilentlyContinue
            }
        }
        'legal' {
            Assert-Initialized
            $legalPath = Join-Path $dockerDirectory 'legal.env'
            if (-not (Test-Path -LiteralPath $legalPath)) {
                throw 'docker/legal.env is missing. Copy legal.env.example to legal.env and fill in the approved values.'
            }
            $environmentArguments = @()
            foreach ($entry in (Read-EnvFile $legalPath).GetEnumerator()) {
                $environmentArguments += @('--env', "$($entry.Key)=$($entry.Value)")
            }
            foreach ($platform in @('web', 'all')) {
                Invoke-Compose (@('run', '--rm', '--no-TTY') + $environmentArguments +
                    @('--env', "LegalDocuments__Platform=$platform", 'api', '--configure-legal-documents'))
            }
        }
        'sql' {
            if ($Arguments.Count -ne 1) { throw 'Usage: .\jokester.ps1 sql <file.sql>' }
            $sqlPath = (Resolve-Path -LiteralPath $Arguments[0]).Path
            # Copy into the container and redirect it there; piping through PowerShell would re-encode UTF-8.
            # Windows PowerShell 5.1 mangles quoted segments containing spaces in native arguments,
            # so the shell command below must not contain any.
            Invoke-Compose @('cp', $sqlPath, 'mysql:/tmp/jokester-run.sql')
            Invoke-Compose @('exec', '-T', 'mysql', 'sh', '-c',
                'mysql --default-character-set=utf8mb4 --user=root --password="$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE" </tmp/jokester-run.sql; status=$?; rm -f /tmp/jokester-run.sql; exit $status')
            Write-Host "Applied $sqlPath"
        }
        'mysql' {
            Invoke-Compose @('exec', 'mysql', 'sh', '-c',
                'exec mysql --default-character-set=utf8mb4 --user=root --password="$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"')
        }
        'backup' {
            $target = if ($Arguments.Count -gt 0) {
                $Arguments[0]
            } else {
                Join-Path $dockerDirectory ("backups/jokester-{0}.sql" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
            }
            $targetDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($target))
            New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            Invoke-Compose @('exec', '-T', 'mysql', 'sh', '-ec',
                'mysqldump --user=root --password="$MYSQL_ROOT_PASSWORD" --single-transaction --routines --triggers --events --hex-blob --default-character-set=utf8mb4 --no-tablespaces --result-file=/tmp/jokester-backup.sql "$MYSQL_DATABASE"')
            Invoke-Compose @('cp', 'mysql:/tmp/jokester-backup.sql', $target)
            Invoke-Compose @('exec', '-T', 'mysql', 'rm', '-f', '/tmp/jokester-backup.sql')
            Write-Host "Database exported to $target"
        }
        'cli' {
            Assert-Initialized
            if ($Arguments.Count -eq 0) { throw 'Usage: .\jokester.ps1 cli <api arguments>' }
            Invoke-Compose (@('run', '--rm', '--no-TTY', 'api') + $Arguments)
        }
        'reset' {
            $answer = Read-Host -Prompt 'This deletes the database, Redis and all uploaded media in Docker volumes. Type RESET to continue'
            if ($answer -cne 'RESET') {
                Write-Host 'Cancelled.'
                return
            }
            Invoke-Compose @('down', '--volumes', '--remove-orphans')
        }
    }
}
finally {
    Pop-Location
}
