# Starts the three NEET BEE projects in separate PowerShell windows.
# Usage:  .\run-all.ps1                      (uses the existing Authentication__Jwt__SigningKey variable)
#         .\run-all.ps1 -SigningKey "<key>"  (sets it for this session first)
param(
    [string]$SigningKey
)

$ErrorActionPreference = "Stop"
$backend = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($SigningKey) { $env:Authentication__Jwt__SigningKey = $SigningKey }
if (-not $env:Authentication__Jwt__SigningKey -or $env:Authentication__Jwt__SigningKey.Length -lt 32) {
    throw "Authentication__Jwt__SigningKey is missing or shorter than 32 characters. Pass -SigningKey or set the variable first."
}

# Start order: auth service first (the gateway forwards to it), then the gateway, then the web app.
$projects = @("AuthenticationService", "ApiGateway", "WebApplication")
foreach ($project in $projects) {
    $projectPath = Join-Path $backend $project
    Start-Process powershell -WorkingDirectory $projectPath -ArgumentList @(
        "-NoExit", "-Command", "dotnet run --project `"$projectPath`""
    )
    Start-Sleep -Seconds 2
}

Write-Host "Started. Web app: http://localhost:5100   Gateway: http://localhost:5000   Auth service: http://localhost:5001"
