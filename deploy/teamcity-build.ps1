[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function ConvertTo-DockerTagPart {
    param([Parameter(Mandatory = $true)][string]$Value)

    $normalized = $Value.ToLowerInvariant() -replace '[^a-z0-9_.-]', '-'
    $normalized = $normalized.Trim('-', '.', '_')
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return 'local'
    }

    return $normalized
}

$checkoutDirectory = $env:TEAMCITY_BUILD_CHECKOUTDIR
if ([string]::IsNullOrWhiteSpace($checkoutDirectory)) {
    $checkoutDirectory = Split-Path -Parent $PSScriptRoot
}

$buildNumber = $env:BUILD_NUMBER
if ([string]::IsNullOrWhiteSpace($buildNumber)) {
    $buildNumber = 'local'
}

$targetPlatform = $env:DOCKER_PLATFORM
if ([string]::IsNullOrWhiteSpace($targetPlatform)) {
    $targetPlatform = 'linux/amd64'
}
if ($targetPlatform -notmatch '^linux/(amd64|arm64)$') {
    throw 'DOCKER_PLATFORM must be linux/amd64 or linux/arm64.'
}

$commit = $env:BUILD_VCS_NUMBER
if ([string]::IsNullOrWhiteSpace($commit)) {
    Push-Location $checkoutDirectory
    try {
        $commit = (& git rev-parse --short=12 HEAD).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "git rev-parse failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$shortCommit = $commit.Substring(0, [Math]::Min(12, $commit.Length))
$tag = ConvertTo-DockerTagPart "$buildNumber-$shortCommit"
$imageReference = "jokester-admin:$tag"
$artifactDirectory = Join-Path $checkoutDirectory 'artifacts'
$archiveName = "jokester-admin-$tag.tar"
$archivePath = Join-Path $artifactDirectory $archiveName

New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null

Push-Location $checkoutDirectory
try {
    Invoke-NativeCommand docker @(
        'build',
        '--pull',
        '--platform', $targetPlatform,
        '--provenance=false',
        '--file', 'Dockerfile',
        '--tag', $imageReference,
        '--label', "org.opencontainers.image.revision=$commit",
        '--label', "org.opencontainers.image.version=$buildNumber",
        '.'
    )

    Invoke-NativeCommand docker @(
        'image', 'inspect',
        '--format', 'Image={{.Id}} Size={{.Size}} User={{.Config.User}} OS={{.Os}} Arch={{.Architecture}}',
        $imageReference
    )
    Invoke-NativeCommand docker @('save', '--output', $archivePath, $imageReference)
}
finally {
    Pop-Location
}

$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText(
    (Join-Path $artifactDirectory 'image-reference.txt'),
    "$imageReference`n",
    $utf8WithoutBom)
[System.IO.File]::WriteAllText(
    (Join-Path $artifactDirectory 'image-archive.txt'),
    "$archiveName`n",
    $utf8WithoutBom)

Write-Host "Built $imageReference"
Write-Host "Archive: $archivePath"
Write-Host "##teamcity[setParameter name='env.JOKESTER_IMAGE_REFERENCE' value='$imageReference']"
