# Roteiro de tela da Fase 11 (parte 11a): atualizar pelo próprio app.
#   11.1 Cópia não instalada: a seção Atualização mostra a versão e "indisponível nesta cópia"; nenhuma conexão com github.com em 90 s.
#   11.2 Sem regressão: o app abre e fecha normalmente (a partida tem os hooks novos do Velopack) e o log não tem ERRO.
# Usa o build publicado em publicado\ (cópia NÃO instalada). Sempre --teste-oculto (StartApp), sem teclas nem cliques reais,
# sem trazer janela para a frente. BackupConfig no início e RestoreConfig no finally (a abertura das Configurações pode gravar
# a versão das Novidades no config.json). Nada aqui roda unins000.exe nem mexe em %LOCALAPPDATA%\Programs\MixerDeAudio.
# Nunca mate este roteiro no meio: rode em primeiro plano, com tempo folgado (cerca de 2 minutos).
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function TextoDe([IntPtr]$h, [string]$id) { $e = FindById $h $id; if ($e) { $e.Current.Name } else { $null } }

$difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
try {
  StopAll
  BackupConfig
  $backupFeito = $true
  $linhasAntes = @(LogLines).Count
  $iniciadosAntes = LogCount 'iniciado'

  # 11.1 Cópia não instalada
  StartApp
  # Espera o app NOVO iniciar (conta so as linhas novas): sem isso o Cmd abaixo pode virar a primeira copia, visivel.
  if (-not (WaitFor { (LogCount 'iniciado') -gt $iniciadosAntes } 10)) { throw 'o app nao iniciou (sem linha "iniciado" no log)' }
  Cmd '--abrir-sem-foco'
  WaitFor { @(Windows).Count -ge 1 } 5 | Out-Null
  $h = @(Windows) | Select-Object -First 1
  if (-not $h) { throw 'a janela nao abriu' }
  OpenSettings $h
  $versao = TextoDe $h 'UpdateVersion'
  $estado = TextoDe $h 'UpdateStatus'
  $btnProcurar = FindById $h 'UpdateCheck'
  $btnAtualizar = FindById $h 'UpdateDownload'
  $btnReiniciar = FindById $h 'UpdateRestart'
  $faixa = FindById $h 'UpdateBanner'
  $textoOk = ($versao -eq 'Versão 1.1.0' -and $estado -like '*indisponível nesta cópia*')
  # Cópia não instalada: sem "Procurar agora" nem "Atualizar" nem "Reiniciar" visíveis (offscreen ou ausentes).
  $semBotoes = (-not $btnAtualizar -or $btnAtualizar.Current.IsOffscreen) -and (-not $btnReiniciar -or $btnReiniciar.Current.IsOffscreen)

  # Nenhuma conexão do app com github.com em 90 s (a conferência automática só viria 60 s depois de abrir).
  $ipsGithub = @()
  try { $ipsGithub = @([Net.Dns]::GetHostAddresses('github.com') | ForEach-Object { $_.ToString() }) } catch {}
  $pidApp = (MainProc).Id
  $conexoes = New-Object System.Collections.Generic.HashSet[string]
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 90) {
    if ((AppProcs).Count -eq 0) { throw 'o app fechou durante a espera de 90 s' }
    foreach ($c in @(Get-NetTCPConnection -OwningProcess $pidApp -ErrorAction SilentlyContinue)) {
      if ($c.RemoteAddress -notin @('127.0.0.1', '::1', '0.0.0.0', '::')) { [void]$conexoes.Add("$($c.RemoteAddress):$($c.RemotePort)") }
    }
    Start-Sleep -Seconds 1
  }
  $contatoGithub = @($conexoes | Where-Object { $ipsGithub -contains ($_ -replace ':\d+$', '') })
  $logRede = @(LogLines | Select-Object -Skip $linhasAntes | Where-Object { $_ -match 'atualizacao: versao' }).Count
  Resultado '11.1 Copia nao instalada' ([bool]($textoOk -and $semBotoes -and $contatoGithub.Count -eq 0 -and $conexoes.Count -eq 0 -and $logRede -eq 0)) "versao='$versao'; estado='$estado'; faixa da janelinha ausente da pagina de configuracoes=$($null -eq $faixa); botoes Atualizar/Reiniciar escondidos=$semBotoes; conexoes externas do app em 90 s=$($conexoes.Count) [$(@($conexoes) -join ', ')]; ips do github.com=$($ipsGithub.Count); contatos com o github=$($contatoGithub.Count)"

  # 11.2 Sem regressão: abre e fecha normalmente, sem ERRO no log
  SettingsBack $h
  Cmd '--fechar'
  $fechou = WaitFor { (LogCount 'janela fechada') -ge 1 } 3
  Cmd '--sair'
  $saiu = WaitFor { (AppProcs).Count -eq 0 } 8
  $novas = @(LogLines | Select-Object -Skip $linhasAntes)
  $erros = @($novas | Where-Object { $_ -match 'ERRO' })
  $temIniciado = @($novas | Where-Object { $_ -match 'iniciado' }).Count -ge 1
  $temSaindo = @($novas | Where-Object { $_ -match 'saindo' }).Count -ge 1
  Resultado '11.2 Sem regressao' ([bool]($fechou -and $saiu -and $temIniciado -and $temSaindo -and $erros.Count -eq 0)) "janela fechou=$fechou; processo saiu=$saiu; log iniciado=$temIniciado saindo=$temSaindo; linhas ERRO=$($erros.Count)"
  $erros | ForEach-Object { "     $_" }
}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
}
Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "diferencas=[$($difFinal -join '; ')]"
"processos restantes: app=$((AppProcs).Count)"
"tempo total do roteiro: $([math]::Round($tempoTotal.Elapsed.TotalSeconds, 1)) s"
if ((AppProcs).Count -ne 0) { $falhas++ }
ReabrirAppDoUsuario
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.'; exit 0 }
