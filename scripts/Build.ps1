param([string]$Dotnet = 'dotnet', [string]$InnoCompiler = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishRoot = Join-Path $projectRoot 'artifacts/publish'
& $Dotnet publish (Join-Path $projectRoot 'src/Stillframe/Stillframe.csproj') -c Release -r win-x64 --self-contained true -o $publishRoot -p:PublishSingleFile=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$sdkRoot = if ([System.IO.Path]::IsPathRooted($Dotnet)) { Split-Path $Dotnet -Parent } else { Split-Path (Get-Command $Dotnet).Source -Parent }
foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $noticePath = Join-Path $sdkRoot $notice
    if (Test-Path $noticePath) { Copy-Item $noticePath (Join-Path $publishRoot $notice) -Force }
}
if ($InnoCompiler) {
    & $InnoCompiler (Join-Path $projectRoot 'installer/Stillframe.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
}
