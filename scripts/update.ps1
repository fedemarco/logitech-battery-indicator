<#
.SYNOPSIS
  Installs the latest Logitech Battery build from GitHub (needs the gh CLI, logged in).
.PARAMETER Release
  Install the latest GitHub Release instead of the latest dev build from main.
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'LogitechBattery'),
    [switch]$Release,
    [string]$Repo = 'fedemarco/logitech-battery-indicator'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) not found. Install it with: winget install GitHub.cli, then run: gh auth login'
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('LogitechBattery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null

try {
    if ($Release) {
        gh release download -R $Repo -p LogitechBattery.exe -D $tmp
        if ($LASTEXITCODE -ne 0) { throw "Failed to download the latest release from $Repo." }
        $tag = gh release view -R $Repo --json tagName --jq .tagName
        if ($LASTEXITCODE -ne 0) { throw 'Failed to read the latest release tag.' }
        $installed = "release $tag"
    }
    else {
        $json = gh run list -R $Repo -w build.yml -b main --status success -L 1 --json databaseId,headSha,displayTitle
        if ($LASTEXITCODE -ne 0) { throw 'Failed to list workflow runs. Are you logged in (gh auth login)?' }
        $runs = @($json | ConvertFrom-Json)
        if ($runs.Count -eq 0) { throw 'No successful dev build found (artifacts expire after 1 day; push to main or re-run the workflow).' }
        $run = $runs[0]
        gh run download $run.databaseId -R $Repo -n LogitechBattery-win-x64 -D $tmp
        if ($LASTEXITCODE -ne 0) { throw "Failed to download the artifact of run $($run.databaseId); it may have expired (1 day retention)." }
        $installed = "dev build $($run.headSha.Substring(0, 7)) - $($run.displayTitle)"
    }

    $exe = Join-Path $tmp 'LogitechBattery.exe'
    if (-not (Test-Path $exe)) { throw 'LogitechBattery.exe not found in the download.' }

    $procs = @(Get-Process -Name LogitechBattery -ErrorAction SilentlyContinue)
    if ($procs.Count -gt 0) {
        Write-Host 'Stopping running LogitechBattery...'
        $procs | Stop-Process -Force
        $procs | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    $target = Join-Path $InstallDir 'LogitechBattery.exe'
    Copy-Item $exe $target -Force
    Start-Process $target

    Write-Host "Installed $installed to $target"
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
