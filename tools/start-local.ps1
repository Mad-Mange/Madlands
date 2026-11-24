# Start the persistent local realm and, unless requested otherwise, the built client.
# This is for the default development PostgreSQL settings, not a public deployment.
[CmdletBinding()]
param([switch]$NoClient)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiUrl = 'http://127.0.0.1:7780/api/v1/login'
$clientPath = Join-Path $repoRoot 'builds/client/Madlands.exe'
$logDir = Join-Path $repoRoot 'builds'

function Test-AccountApi {
    # Login is POST-only; a 405 on GET confirms that the account API route is listening.
    try {
        $response = Invoke-WebRequest -Uri $apiUrl -Method Get -UseBasicParsing -TimeoutSec 2
        return [int]$response.StatusCode -eq 405
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode -eq 405
        }
        return $false
    }
}

function Test-LocalPort([int]$Port) {
    $connection = [System.Net.Sockets.TcpClient]::new()
    try {
        $pending = $connection.ConnectAsync('127.0.0.1', $Port)
        return $pending.Wait(1000) -and $connection.Connected
    }
    catch { return $false }
    finally { $connection.Dispose() }
}

function Invoke-Compose([string[]]$Arguments, [switch]$Quiet) {
    # Windows PowerShell can treat native stderr as a terminating error when the caller uses Stop,
    # even for pg_isready's ordinary "not ready yet" result. Check the native exit code instead.
    $ErrorActionPreference = 'Continue'
    if ($Quiet) {
        & docker compose -f (Join-Path $repoRoot 'deploy/docker-compose.yml') @Arguments 2>$null | Out-Null
    }
    else {
        & docker compose -f (Join-Path $repoRoot 'deploy/docker-compose.yml') @Arguments | Out-Host
    }
    return $LASTEXITCODE
}

function Start-LocalRealm {
param([switch]$NoClient)

if (-not (Test-AccountApi)) {
    if (Test-LocalPort 7780) {
        throw 'Port 7780 is already in use, but the Madlands account API did not respond. No server was started.'
    }
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker is not installed or not on PATH. Start Docker Desktop and try again.'
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET is not installed or not on PATH. Install the .NET 10 SDK and try again.'
    }

    Write-Host 'Starting the local PostgreSQL service...'
    $composeResult = Invoke-Compose -Arguments @('up', '-d', 'postgres')
    if ($composeResult -ne 0) { throw 'Could not start PostgreSQL. Check that Docker Desktop is running.' }
    # A container can be running before PostgreSQL is ready, or its host port can be unavailable.
    $dbDeadline = (Get-Date).AddSeconds(60)
    $databaseReady = $false
    while ((Get-Date) -lt $dbDeadline) {
        $readyResult = Invoke-Compose -Arguments @('exec', '-T', 'postgres', 'pg_isready', '-U', 'madlands', '-d', 'madlands') -Quiet
        if ($readyResult -eq 0 -and (Test-LocalPort 15432)) { $databaseReady = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $databaseReady) { throw 'PostgreSQL did not become reachable at 127.0.0.1:15432. Check Docker and the port mapping.' }

    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $serverLog = Join-Path $logDir 'local-server.log'
    $errorLog = Join-Path $logDir 'local-server-error.log'
    $server = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', 'server/Madlands.Server', '-c', 'Release', '--', '--bind', '127.0.0.1') `
        -WorkingDirectory $repoRoot -WindowStyle Hidden -RedirectStandardOutput $serverLog -RedirectStandardError $errorLog -PassThru
    Write-Host 'Waiting for the game server to start...'
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline -and -not (Test-AccountApi)) {
        $server.Refresh()
        if ($server.HasExited) { throw "The server stopped during startup. Check $serverLog and $errorLog." }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-AccountApi)) { throw "The account API did not respond within 90 seconds. Check $serverLog and $errorLog." }
    Write-Host 'Local game server is ready.'
}
else {
    Write-Host 'The local account API is already running.'
}

if (-not $NoClient) {
    if (-not (Test-Path -LiteralPath $clientPath -PathType Leaf)) {
        throw "The built client was not found at $clientPath. Build it in Unity first."
    }
    Write-Host 'Starting Madlands. Use 127.0.0.1 in the server field.'
    Start-Process -FilePath $clientPath -WorkingDirectory (Split-Path -Parent $clientPath)
}
}

# Dot-sourcing exposes the functions for tests without starting services.
if ($MyInvocation.InvocationName -ne '.') { Start-LocalRealm -NoClient:$NoClient }
