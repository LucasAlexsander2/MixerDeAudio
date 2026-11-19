# Roteiro de tela da Fase 2 (casos 2.1 a 2.8; o 2.9 é o medir.ps1 -ComSom).
# Usa o build publicado em publicado\ e os tocadores de teste (silêncio) de tocar.cs.
# Sem teclas nem cliques reais, sem trazer janela para a frente. NUNCA muda volume ou mudo de app que não seja
# tocador de teste (os apps reais só são lidos). Encerra o app e os tocadores sempre, também em caso de erro.
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Fmt($v) { if ($null -eq $v) { 'nulo' } else { ([double]$v).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) } }
function JanelaAtual { @(Windows) | Select-Object -First 1 }
function AbrirJanela {
  Cmd '--abrir-sem-foco'
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  JanelaAtual
}
# Volumes dos apps que não são tocadores de teste, para provar que ninguém mexeu neles.
function VolumesReais { (Sessoes | Where-Object { -not $_.Nome.StartsWith('MX-teste') } | ForEach-Object { '{0}|{1}|{2}' -f $_.Pid, (Fmt $_.Volume), $_.Mudo } | Sort-Object) -join ';' }

try {
  StopAll
  StopPlayers
  PrepararFerramentas
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  # 2.1 Lista ao abrir
  $pA = StartPlayer 'MX-teste A'
  $reaisAntes = VolumesReais
  StartApp
  Start-Sleep -Seconds 2
  $h = AbrirJanela
  $achou = WaitFor { (@(Rows $h)) -contains 'MX-teste A' } 3
  Start-Sleep -Milliseconds 800  # tempo dos ícones
  $volTela = RowVolume $h 'MX-teste A'
  $sA = SessionOf $pA.Id
  $linhas = @(Rows $h)
  $ordemOk = ($linhas[-1] -eq 'Sons do sistema')
  $semSistema = @($linhas | Where-Object { $_ -ne 'Sons do sistema' })
  $ordenado = @($semSistema | Sort-Object { $_.ToLowerInvariant() })
  $ordemOk = $ordemOk -and (($semSistema -join '|').ToLowerInvariant() -eq (($ordenado -join '|').ToLowerInvariant()))
  $m = [regex]::Matches((LogLines | Out-String), 'sessoes carregadas \(apps=(\d+), sessoes=(\d+), (\d+) ms\)')
  $ms = if ($m.Count -gt 0) { [int]$m[$m.Count - 1].Groups[3].Value } else { -1 }
  $apps = if ($m.Count -gt 0) { [int]$m[$m.Count - 1].Groups[1].Value } else { -1 }
  $shot = Shot $h 'f2-janela'
  Resultado '2.1 Lista ao abrir' ([bool]($achou -and $volTela -eq [math]::Round($sA.Volume * 100) -and $m.Count -ge 1 -and $ordemOk)) "linha MX-teste A=$achou volume tela=$volTela volume sessoes=$(Fmt $sA.Volume) ordem ok=$ordemOk linhas=$($linhas.Count) log apps=$apps tempo de abertura=$ms ms (esperado < 300) captura=$shot"
  "     linhas: $($linhas -join ' | ')"

  # 2.2 Slider muda o app
  SetRowVolume $h 'MX-teste A' 30
  $okVol = WaitFor { [math]::Abs((SessionOf $pA.Id).Volume - 0.30) -le 0.01 } 2
  $reaisDepois = VolumesReais
  Resultado '2.2 Slider muda o app' ($okVol -and $reaisAntes -eq $reaisDepois) "sessoes=$(Fmt (SessionOf $pA.Id).Volume) (esperado 0,30) outros apps iguais=$($reaisAntes -eq $reaisDepois)"

  # 2.3 Mudo
  ToggleRowMute $h 'MX-teste A'
  $mudo1 = WaitFor { (SessionOf $pA.Id).Mudo } 2
  $nome1 = RowMuteName $h 'MX-teste A'
  ToggleRowMute $h 'MX-teste A'
  $mudo2 = WaitFor { -not (SessionOf $pA.Id).Mudo } 2
  $nome2 = RowMuteName $h 'MX-teste A'
  Resultado '2.3 Mudo' ($mudo1 -and $mudo2 -and $nome1 -eq 'Ativar som de MX-teste A' -and $nome2 -eq 'Silenciar MX-teste A') "mudo apos 1o toggle=$mudo1 apos 2o=$($mudo2 -eq $true) nome1='$nome1' nome2='$nome2'"

  # 2.4 Mudança de fora
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $rc = DefinirSessao $pA.Id '0.8'
  $viu = WaitFor { (RowVolume $h 'MX-teste A') -eq 80 } 1.5
  $tempo = [math]::Round($sw.Elapsed.TotalSeconds, 2)
  Start-Sleep -Milliseconds 500
  $volFinal = (SessionOf $pA.Id).Volume
  Resultado '2.4 Mudanca de fora' ($rc -eq 0 -and $viu -and [math]::Abs($volFinal - 0.80) -le 0.011) "linha mostra 80 em ${tempo}s=$viu sessoes depois=$(Fmt $volFinal) (esperado 0,80, sem gravar de volta) valor texto='$(RowValueText $h 'MX-teste A')'"

  # 2.5 App novo e app que fecha
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $pB = StartPlayer 'MX-teste B' -B
  $apareceu = WaitFor { (@(Rows $h)) -contains 'MX-teste B' } 4
  $tAparece = [math]::Round($sw.Elapsed.TotalSeconds, 1)
  $linhas = @(Rows $h)
  $ia = [array]::IndexOf($linhas, 'MX-teste A'); $ib = [array]::IndexOf($linhas, 'MX-teste B')
  $ordemB = ($ib -eq $ia + 1)
  $pB.Kill()
  $sw.Restart()
  $sumiu = WaitFor { -not ((@(Rows $h)) -contains 'MX-teste B') } 15
  $tSome = [math]::Round($sw.Elapsed.TotalSeconds, 1)
  Resultado '2.5 App novo e que fecha' ($apareceu -and $ordemB -and $sumiu) "B apareceu=$apareceu (do inicio do processo ate a linha: ${tAparece}s, a espera do tocador inclui ~1 s) ordem apos A=$ordemB sumiu apos fechar=$sumiu em ${tSome}s (limite 10 s)"

  # 2.6 Mesmo .exe, uma linha
  $pA2 = StartPlayer 'MX-teste A2'
  Cmd '--fechar'
  WaitFor { @(Windows).Count -eq 0 } 2 | Out-Null
  $h = AbrirJanela
  WaitFor { @(Rows $h | Where-Object { $_ -like 'MX-teste*' }).Count -ge 1 } 3 | Out-Null
  $mx = @(Rows $h | Where-Object { $_ -like 'MX-teste*' })
  if ($mx.Count -ge 1) { SetRowVolume $h $mx[0] 50 }
  $ok50 = WaitFor { [math]::Abs((SessionOf $pA.Id).Volume - 0.5) -le 0.01 -and [math]::Abs((SessionOf $pA2.Id).Volume - 0.5) -le 0.01 } 2
  Resultado '2.6 Mesmo .exe, uma linha' ($mx.Count -eq 1 -and $ok50) "linhas MX-teste=$($mx.Count) ($($mx -join ', ')) volume A=$(Fmt (SessionOf $pA.Id).Volume) A2=$(Fmt (SessionOf $pA2.Id).Volume) (esperado 0,50 nos dois)"

  # 2.7 Fechar libera tudo
  $carregadasAntes = LogCount 'sessoes carregadas'
  Cmd '--fechar'
  $fechou = WaitFor { @(Windows).Count -eq 0 } 2
  Start-Sleep -Milliseconds 500
  $rc1 = DefinirSessao $pA.Id '0.6'
  $rc2 = DefinirSessao $pA2.Id '0.7' 'mudo'
  Start-Sleep -Seconds 2
  $vivo = (AppProcs).Count -eq 1
  $erros = @(LogLines | Where-Object { $_ -match 'ERRO' })
  Resultado '2.7 Fechar libera tudo' ($fechou -and $rc1 -eq 0 -and $rc2 -eq 0 -and $vivo -and $erros.Count -eq 0 -and (LogCount 'sessoes carregadas') -eq $carregadasAntes) "fechou=$fechou definir rc=$rc1/$rc2 processo vivo=$vivo erros no log=$($erros.Count) novas leituras=$((LogCount 'sessoes carregadas') - $carregadasAntes)"
  $erros | ForEach-Object { "     $_" }

  # Encerrar tocadores e app antes do 2.8 (o roteiro da Fase 1 reinicia o app e apaga o log).
  StopPlayers
  StopAll
  $restoAntes8 = (PlayersAbertos) + (AppProcs).Count

  # 2.8 Sem regressão
  $saidaF1 = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\roteiro-f1.ps1" 2>&1 | Out-String
  $okF1 = ($LASTEXITCODE -eq 0) -and ([regex]::Matches($saidaF1, '(?m)^OK    1\.\d')).Count -ge 4
  Resultado '2.8 Sem regressao' $okF1 "roteiro-f1 saida: $((($saidaF1 -split "`r?`n") | Where-Object { $_ -match '^(OK|FALHA|Todos|FALHAS)' }) -join ' / ')"
}
finally {
  StopPlayers
  StopAll
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
}
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
