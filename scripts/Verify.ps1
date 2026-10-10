param([string]$Dotnet = 'dotnet', [string]$Executable = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$reportRoot = Join-Path $projectRoot 'artifacts/verification'
New-Item -ItemType Directory -Force $reportRoot | Out-Null
& $Dotnet build (Join-Path $projectRoot 'Stillframe.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $Dotnet test (Join-Path $projectRoot 'tests/Stillframe.Tests/Stillframe.Tests.csproj') -c Release --no-build --results-directory $reportRoot --logger 'trx;LogFileName=core-results.trx'
if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed.' }
$exe = if ($Executable) { $Executable } else { Join-Path $projectRoot 'src/Stillframe/bin/Release/net10.0-windows/Stillframe.exe' }
$profile = Join-Path $reportRoot ('profile-' + [Guid]::NewGuid().ToString('N'))
if ([System.IO.Path]::IsPathRooted($Dotnet)) { $env:DOTNET_ROOT = Split-Path $Dotnet -Parent }
$appArguments = '--profile "{0}" --verify "{1}" --diagnostics' -f $profile,$reportRoot
$appProcess = Start-Process -FilePath $exe -ArgumentList $appArguments -WindowStyle Hidden -PassThru
if (-not $appProcess.WaitForExit(60000)) { throw 'Integration checks exceeded 60 seconds. Inspect the test profile log.' }
$results = Get-Content (Join-Path $reportRoot 'integration-results.json') -Raw | ConvertFrom-Json
$failures = @($results | Where-Object { -not $_.Passed })
$results | Format-Table Name,Passed -AutoSize
if ($failures.Count -gt 0) { throw "$($failures.Count) integration checks failed." }
Copy-Item (Join-Path $profile 'diagnostics.json') (Join-Path $reportRoot 'desktop-diagnostics.json') -Force
Write-Host 'All Stillframe checks passed.'
