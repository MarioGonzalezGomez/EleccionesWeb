param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Url = "http://localhost:5177",
    [switch]$SelfContained,
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts\manual-run"
$serverOutput = Join-Path $artifactsRoot "server"
$clientOutput = Join-Path $artifactsRoot "client"
$serverProject = Join-Path $repoRoot "src\Elecciones.Web\Elecciones.Web.csproj"
$clientProject = Join-Path $repoRoot "tools\WebClientLauncher\WebClientLauncher.csproj"

if (Test-Path $artifactsRoot)
{
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

New-Item -Path $serverOutput -ItemType Directory -Force | Out-Null
New-Item -Path $clientOutput -ItemType Directory -Force | Out-Null

$selfContainedFlag = if ($SelfContained) { "true" } else { "false" }
$singleFileFlag = if ($SingleFile) { "true" } else { "false" }

Write-Host "Publicando servidor (Elecciones.Web)..."
dotnet publish $serverProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedFlag `
    /p:PublishSingleFile=$singleFileFlag `
    /p:PublishTrimmed=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $serverOutput
if ($LASTEXITCODE -ne 0)
{
    throw "Fallo la publicacion del servidor."
}

Write-Host "Publicando lanzador cliente (WebClientLauncher)..."
dotnet publish $clientProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedFlag `
    /p:PublishSingleFile=$singleFileFlag `
    /p:PublishTrimmed=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o $clientOutput
if ($LASTEXITCODE -ne 0)
{
    throw "Fallo la publicacion del cliente."
}

$operationUrl = ($Url.TrimEnd('/') + "/operacion")

$startServerCmd = @"
@echo off
setlocal
cd /d "%~dp0server"
set ASPNETCORE_URLS=$Url
echo Iniciando servidor en %ASPNETCORE_URLS% ...
EleccionesWeb.exe
echo.
echo Servidor detenido.
pause
"@
Set-Content -Path (Join-Path $artifactsRoot "start-server.cmd") -Value $startServerCmd -Encoding ASCII

$startClientCmd = @"
@echo off
setlocal
cd /d "%~dp0client"
WebClientLauncher.exe $operationUrl
"@
Set-Content -Path (Join-Path $artifactsRoot "start-client.cmd") -Value $startClientCmd -Encoding ASCII

$startBothCmd = @"
@echo off
setlocal
start "Servidor EleccionesWeb" cmd /k "%~dp0start-server.cmd"
timeout /t 2 >nul
call "%~dp0start-client.cmd"
"@
Set-Content -Path (Join-Path $artifactsRoot "start-both.cmd") -Value $startBothCmd -Encoding ASCII

$readme = @"
Manual run bundle generado.

Archivos:
- start-server.cmd : levanta solo el servidor.
- start-client.cmd : abre el navegador en /operacion.
- start-both.cmd   : levanta servidor y abre cliente.

Ruta servidor publicado:
$serverOutput

Ruta cliente publicado:
$clientOutput
"@
Set-Content -Path (Join-Path $artifactsRoot "README.txt") -Value $readme -Encoding ASCII

Write-Host ""
Write-Host "Listo. Bundle generado en: $artifactsRoot"
Write-Host "Usa start-both.cmd para la prueba manual."
