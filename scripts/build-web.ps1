#!/usr/bin/env pwsh
# Builds the web shell.
#
# Frontend commands run from web/, never from the repository root (Repo Rule 3, PRD §3.1), which is
# why this script pushes into web/ rather than passing --prefix. dotnet test deliberately does not
# call it: that would make the .NET suite depend on Node and on node_modules being populated.

$ErrorActionPreference = 'Stop'

$web = Join-Path $PSScriptRoot '..' 'web'

Push-Location $web
try {
    $installCommand = if (Test-Path 'package-lock.json') { 'npm ci' } else { 'npm install' }
    if ($installCommand -eq 'npm ci') { npm ci } else { npm install }
    if ($LASTEXITCODE -ne 0) { throw "$installCommand failed with exit code $LASTEXITCODE" }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Write-Output 'web shell built.'
