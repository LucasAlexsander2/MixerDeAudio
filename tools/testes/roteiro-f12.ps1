# Roteiro de tela da Fase 12: brilho do monitor pelo celular (so o que depende do app de verdade; a conversao e o agrupamento ficam nos testes unitarios).
#   Casos 12.1 a 12.3: comando -> worker -> estado, agrupamento de uma rajada pela rede e sem regressao (telas apagar).
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone). Com --teste-pc o RecordingPc guarda o brilho em
# memoria e so grava "teste-pc: brilho ..." no log: o DDC real nunca e lido nem mudado. Antes de qualquer comando o roteiro confere a linha
# "teste-pc: modo de teste ligado" no log (AssertModoTestePc) e o celular.exe confere "teste":true no estado; faltando um dos dois, aborta sem enviar.
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao, com prazo folgado).
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
$script:inicioRoteiro = Get-Date
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

  $porta = LigarFerramentas @('monitores')
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  AssertModoTestePc
  $pa = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or -not $pa.Chave) { throw "o pareamento falhou ($($pa.Status))" }

  # 12.1 Comando -> worker -> estado
  WaitFor { $x = CelularEstado $porta $pa.Chave; $null -ne $x -and @($x.pc.brilho).Count -ge 1 } 5 | Out-Null
  $e0 = CelularEstado $porta $pa.Chave
  $inicial = if ($e0 -and $e0.pc.brilho) { @($e0.pc.brilho)[0].valor } else { $null }
  $rc = CelularFerramenta $porta $pa.Chave '{"t":"brilho","tela":0,"valor":30}'
  $logOk = WaitFor { @(LogTestePc '^brilho 0 30$').Count -ge 1 } 5
  $e1 = CelularEstado $porta $pa.Chave
  $valor = if ($e1 -and $e1.pc.brilho) { @($e1.pc.brilho)[0].valor } else { $null }
  Resultado '12.1 Comando, worker e estado' ([bool]($rc -eq 0 -and $logOk -and $inicial -eq 50 -and $valor -eq 30)) "estado inicial brilho[0].valor=$inicial (esperado 50); comando enviado=$($rc -eq 0); log 'teste-pc: brilho 0 30'=$logOk; estado seguinte brilho[0].valor=$valor (esperado 30)"

  # 12.2 Agrupamento: rajada de 20 valores (1 a 20) no mesmo canal
  AssertModoTestePc
  $antes = @(LogTestePc '^brilho 0 ').Count
  $rc = & (CelularExe) brilhorajada $porta $pa.Chave 0 1 20 | Out-Null; $rc = $LASTEXITCODE
  $ultimo = WaitFor { @(LogTestePc '^brilho 0 20$').Count -ge 1 } 5
  Start-Sleep -Milliseconds 800
  $linhas = @(LogTestePc '^brilho 0 ' | Select-Object -Skip $antes)
  $textos = ($linhas | ForEach-Object { $_.Texto }) -join ' | '
  $e2 = CelularEstado $porta $pa.Chave
  $valor2 = if ($e2 -and $e2.pc.brilho) { @($e2.pc.brilho)[0].valor } else { $null }
  Resultado '12.2 Agrupamento da rajada' ([bool]($rc -eq 0 -and $ultimo -and $linhas.Count -ge 1 -and $linhas.Count -le 3 -and $linhas[$linhas.Count - 1].Texto -eq 'brilho 0 20' -and $valor2 -eq 20)) "rajada enviada=$($rc -eq 0); linhas de brilho no log=$($linhas.Count) (esperado de 1 a 3, de 20 pedidos); ultima=20: $ultimo; linhas: [$textos]; estado brilho[0].valor=$valor2 (esperado 20)"

  # 12.3 Sem regressao: telas apagar
  AssertModoTestePc
  $rc = CelularFerramenta $porta $pa.Chave '{"t":"telas","acao":"apagar"}'
  $okTelas = WaitFor { @(LogTestePc '^telas apagar$').Count -ge 1 } 3
  Resultado '12.3 Sem regressao (telas apagar)' ([bool]($rc -eq 0 -and $okTelas)) "comando enviado=$($rc -eq 0); log 'teste-pc: telas apagar'=$okTelas"

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
