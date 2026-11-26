# Roteiro de tela da Fase 15a (Inicio e Som): so o que depende do PC de verdade (a GPU % no info; o resto esta nos testes unitarios).
#   Caso 15a.1: assinar o info, ler as mensagens e conferir o campo gpu (0 a 100, ou ausente) sem estragar o resto do info.
# Usa o build publicado em publicado\ (publique antes). Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone, sem aviso do firewall).
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao).
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}

$difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
try {
  StopAll
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"

  # o grupo "estado" liga o info
  $porta = LigarFerramentas @('estado')
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  $pa = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or -not $pa.Chave) { throw "o pareamento falhou (status $($pa.Status))" }

  # 15a.1 Contador real da GPU: assinar o info por 7 s (3 ou 4 mensagens a cada 2 s) e cancelar
  $ri = CelularInfo $porta $pa.Chave 7 3
  $infos = @($ri.Info)
  $coerente = $infos.Count -ge 3
  $comGpu = @(); $valores = @()
  foreach ($i in $infos) {
    $temGpu = $null -ne $i.PSObject.Properties['gpu']
    if ($temGpu) {
      $g = [double]$i.gpu
      $valores += $g
      if ($g -lt 0 -or $g -gt 100 -or $g -ne [math]::Floor($g)) { $coerente = $false }
    }
    # o resto do info nao muda (D13: o campo gpu e o unico acrescentado)
    if ($null -eq $i.cpu -or [double]$i.cpu -lt 0 -or [double]$i.cpu -gt 100 -or [double]$i.mem.total -le 0 -or $null -eq $i.disco) { $coerente = $false }
  }
  $presentes = $valores.Count
  $motivo = if ($presentes -eq 0) { 'gpu ausente em todas as mensagens (contador "GPU Engine" indisponivel neste PC, aceito pela D13)' } else { "gpu presente em $presentes de $($infos.Count) mensagens: $($valores -join ' ')" }
  Resultado '15a.1 GPU no info' ([bool]($ri.Codigo -eq 0 -and $coerente -and @($ri.Depois).Count -eq 0)) "assinar info por 7 s (codigo de saida $($ri.Codigo)): $($infos.Count) mensagens; $motivo; depois do off: $(@($ri.Depois).Count) mensagens (esperado 0)"

  $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log: $($errosLog.Count)"
  $errosLog | ForEach-Object { "     $_" }
  if ($errosLog.Count -gt 0) { $script:falhas++ }
  Cmd '--fechar'
}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
