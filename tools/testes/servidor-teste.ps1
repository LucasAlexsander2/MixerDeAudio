# Servidor de teste (Fase 15.0): liga o app publicado com --teste-oculto --celular-local --teste-pc e um codigo conhecido, para o Claude conferir
# o novo front (/novo) no navegador embutido (PC de teste de verdade: toda acao so grava "teste-pc: <acao>" no log).
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\servidor-teste.ps1 [-Codigo 123456] [-LimiteHoras 12]
# Imprime PORTA=, CODIGO=, PARAR= e LOG=, depois PRONTO. Fica esperando o arquivo PARAR (crie-o, por exemplo com
# `New-Item <caminho do PARAR>`) e entao encerra o app, restaura o config.json e o registro e reabre o app do usuario.
# SEGURANCA: BackupConfig antes de tudo e RestoreConfig no finally (tambem com erro ou Ctrl+C); o app nunca e morto a forca
# (StopAll usa --sair). Um arquivo "servidor-teste.ativo" marca a execucao: se ele ja existir ao comecar, a execucao anterior nao
# restaurou o config.json real e este script recusa (nao deixa o config de teste virar o "backup"); a copia do config real fica em
# %TEMP%\mixer-check\backup-config (config-AAAAMMDD-HHMMSS-mmm.json).
# O publicado\ precisa estar atualizado: dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado
# (o script confere e recusa se algum arquivo de Remote\Web ou .cs for mais novo que o publicado\MixerDeAudio.dll; -SemConferirPublicado ignora).
param([string]$Codigo = '123456', [double]$LimiteHoras = 12, [switch]$SemConferirPublicado)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$parar = "$script:out\parar-servidor.txt"
$ativo = "$script:out\servidor-teste.ativo"
$backupFeito = $false
$sucesso = $false
$difFinal = @('o RestoreConfig nao rodou')

if (Test-Path $ativo) {
  "RECUSADO: $ativo existe: a execucao anterior nao terminou de restaurar o config.json real."
  "Confira $script:cfgFile e a copia mais recente em $script:bkDir antes de apagar esse arquivo."
  exit 2
}
if (-not (Test-Path $script:exe)) { 'RECUSADO: publicado\MixerDeAudio.exe nao existe; publique antes (veja o cabecalho).'; exit 2 }
if (-not $SemConferirPublicado) {
  $dll = Get-Item "$script:root\publicado\MixerDeAudio.dll" -ErrorAction SilentlyContinue
  $marco = if ($dll) { $dll.LastWriteTime } else { (Get-Item $script:exe).LastWriteTime }
  $novo = @(Get-ChildItem "$script:root\MixerDeAudio" -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in '.cs', '.js', '.css', '.html', '.xaml', '.json', '.webmanifest' -and $_.LastWriteTime -gt $marco } |
    Select-Object -First 3)
  if ($novo.Count -gt 0) {
    "RECUSADO: publicado\ esta desatualizado (ex.: $($novo[0].FullName.Substring($script:root.Length + 1))). Publique antes (veja o cabecalho)."
    exit 2
  }
}

try {
  Remove-Item $parar -Force -ErrorAction SilentlyContinue
  StopAll
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); copia em $script:bkDir"
  Set-Content $ativo "iniciado em $(Get-Date -Format s); copia do config real em $script:bkDir" -Encoding utf8

  $porta = LigarCelularTeste $Codigo
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  AssertModoTestePc
  "PORTA=$porta"
  "CODIGO=$Codigo"
  "PARAR=$parar"
  "LOG=$script:log"
  "PRONTO (abra http://127.0.0.1:$porta/novo ; para encerrar, crie o arquivo PARAR)"

  $limite = [Diagnostics.Stopwatch]::StartNew()
  while (-not (Test-Path $parar)) {
    if ($limite.Elapsed.TotalHours -ge $LimiteHoras) { "limite de $LimiteHoras h atingido; encerrando"; break }
    if ((AppProcs).Count -eq 0) { 'o app de teste fechou sozinho; encerrando'; break }
    Start-Sleep -Seconds 1
  }
  $sucesso = $true
}
catch {
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  StopAll
  if ($backupFeito) {
    $difFinal = @(RestoreConfig)
    if ($difFinal.Count -eq 0) { Remove-Item $ativo -Force -ErrorAction SilentlyContinue }
  }
  Remove-Item $parar -Force -ErrorAction SilentlyContinue
  'RESTAURADO: config.json={0}; Run MixerDeAudio={1}; diferencas=[{2}]' -f $(if ($script:backup.ConfigExiste) { 'devolvido' } else { 'nao existia (apagado)' }), (RegTexto $script:backup.Run), ($difFinal -join '; ')
  if ($backupFeito -and $difFinal.Count -gt 0) { "ATENCAO: $ativo mantido; copia do config real em $script:bkDir" }
  ReabrirAppDoUsuario
}
if (-not $sucesso -or $difFinal.Count -gt 0) { exit 1 }
