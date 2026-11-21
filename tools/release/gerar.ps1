<#
.SYNOPSIS
  Gera o instalador (Inno Setup) do Mixer de Audio: dist\MixerDeAudio-<versao>-x64-setup.exe.
.DESCRIPTION
  A versao vem do <Version> de MixerDeAudio\MixerDeAudio.csproj (unica fonte).
  Publica em dist\app (nunca em publicado\, que e o app dos testes) e compila instalador\MixerDeAudio.iss.
.PARAMETER Saida  Pasta de saida (padrao dist).
.PARAMETER Iscc   Caminho do ISCC.exe (padrao: procura no PATH e nas pastas do Inno Setup 6).
#>
[CmdletBinding()]
param([string]$Saida = 'dist', [string]$Iscc)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$csproj = Join-Path $root 'MixerDeAudio\MixerDeAudio.csproj'
$iss = Join-Path $root 'instalador\MixerDeAudio.iss'

function Falha([string]$msg) { Write-Host "ERRO: $msg" -ForegroundColor Red; exit 1 }
function Rodar([string]$que, [scriptblock]$cmd) {
    $ErrorActionPreference = 'Continue'   # stderr de programa nativo nao deve virar excecao no PS 5.1
    $out = & $cmd 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { $out | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }; Falha "$que falhou (codigo $LASTEXITCODE)." }
}

$versao = (& dotnet msbuild $csproj -getProperty:Version 2>&1 | ForEach-Object { "$_" } | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($versao)) { Falha 'nao consegui ler o <Version> do MixerDeAudio.csproj.' }
if ($versao -notmatch '^\d+\.\d+\.\d+$') { Falha "versao '$versao' invalida: use so numeros (ex.: 1.0.0)." }

if (-not $Iscc) {
    $achou = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($achou) { $Iscc = $achou.Source }
    else {
        $cands = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
        $Iscc = $cands | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    }
}
if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc)) { Falha 'ISCC.exe (Inno Setup 6) nao encontrado. Instale com: winget install JRSoftware.InnoSetup (ou passe -Iscc <caminho>).' }

$saidaAbs = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Saida)) { $Saida } else { Join-Path $root $Saida }))
if ($saidaAbs.TrimEnd('\') -ieq (Join-Path $root 'publicado').TrimEnd('\')) { Falha 'a saida nao pode ser publicado\.' }
New-Item -ItemType Directory -Force -Path $saidaAbs | Out-Null
Write-Host "Mixer de Audio $versao -> $saidaAbs"

$pub = Join-Path $saidaAbs 'app'
if (Test-Path -LiteralPath $pub) { Remove-Item -LiteralPath $pub -Recurse -Force }
Write-Host 'publicando...'
Rodar 'dotnet publish' { dotnet publish $csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o $pub -v q -nologo }
if (-not (Test-Path -LiteralPath (Join-Path $pub 'MixerDeAudio.exe'))) { Falha 'o publish nao gerou MixerDeAudio.exe.' }

$setup = "MixerDeAudio-$versao-x64-setup.exe"
$setupAbs = Join-Path $saidaAbs $setup
if (Test-Path -LiteralPath $setupAbs) { Remove-Item -LiteralPath $setupAbs -Force }
Write-Host 'compilando o instalador...'
Rodar 'ISCC' { & $Iscc /Q "/DAppVersion=$versao" "/DFonte=$pub" "/DSaida=$saidaAbs" $iss }
if (-not (Test-Path -LiteralPath $setupAbs)) { Falha "o Inno Setup nao gerou $setup." }
'{0}  {1:N1} MB' -f $setupAbs, ((Get-Item -LiteralPath $setupAbs).Length / 1MB)
