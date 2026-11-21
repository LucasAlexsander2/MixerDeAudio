# Roteiro de tela da Fase 7: controles de musica por app.
#   -Parte a : casos 7.1 a 7.4 (linha com midia, botoes, linha so de midia, sem regressao curta).
#   -Parte b : casos 7.5 a 7.7 (celular) e 7.8 (pagina servida: tamanho e sem URL externa).
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando do app
# (--abrir-sem-foco), UI Automation por padroes (Invoke, leitura) e a fonte de midia de teste (tocar.cs com --midia).
# SEGURANCA (P3): as sessoes de midia reais (Opera, Spotify...) so sao LIDAS. Os botoes so sao acionados (Invoke) em linhas
# "MX-teste*" ou "tocar*"; MidiaInvoke recusa qualquer outra com throw. BackupConfig/RestoreConfig guardam e devolvem o
# config.json e o registro. O roteiro confere que a padrao, o volume e mudo gerais e os apps reais (volume, mudo e saida)
# ficaram iguais. Nao troca a saida padrao nem mexe no mudo geral. Ao fim, nenhum processo tocar/tocar-b pode sobrar.
# Roteiro so do que a fase fez: nao roda roteiros de fases anteriores nem a medicao. As fontes de midia sao ligadas uma vez
# (MX-teste A nos casos 7.1 a 7.4; tocar-b so no 7.3) e todas as esperas sao por condicao (WaitFor), nunca pausas fixas.
param([ValidateSet('a', 'b')][string]$Parte = 'a')
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
$inv = [Globalization.CultureInfo]::InvariantCulture
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Pulado([string]$caso, [string]$motivo) { 'PULADO {0}: {1}' -f $caso, $motivo }
function AbrirJanela([string]$cmd = '--abrir-sem-foco') {
  Cmd $cmd
  WaitFor { @(Windows).Count -ge 1 } 4 | Out-Null
  @(Windows) | Select-Object -First 1
}
function PadraoAtual { Devices | Where-Object { $_.Padrao } | Select-Object -First 1 }
function Fmt([double]$v) { $v.ToString('0.00', $inv) }
# Apps que nao sao tocadores de teste: pid, nome, volume e mudo (so leitura) e a escolha de saida de cada um.
function AppsReais {
  @(Sessoes | Where-Object { $_.Nome -notlike 'MX-teste*' -and $_.Pid -gt 0 } | ForEach-Object {
      "{0}|{1}|{2}|{3}|{4}" -f $_.Pid, $_.Nome, (Fmt $_.Volume), $_.Mudo, (RotaLer $_.Pid) } | Sort-Object)
}
function GeralAtual { $d = PadraoAtual; "{0}|{1}|{2}" -f $d.Id, (Fmt $d.Volume), $d.Mudo }

if ($Parte -eq 'b') {
  # Parte 7b: celular (casos 7.5 a 7.7 e a pagina servida). Servidor so em 127.0.0.1 (--celular-local); celular.cs recusa o que
  # nao for fonte de teste. As fontes (MX-teste A e tocar-b so de midia) sao ligadas uma vez para todos os casos.
  $logA = "$script:out\midia-a.log"; $logB = "$script:out\midia-b.log"; $arq = "$script:out\celular-b.txt"
  $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $seg = $null
  try {
    StopAll
    StopPlayers
    PrepararFerramentas
    BackupConfig
    $backupFeito = $true
    $reaisAntes = AppsReais
    $pA = StartMidia 'MX-teste A' $logA
    $pB = StartMidia 'tocar-b' $logB -B -SemAudio
    $porta = LigarCelularTeste '123456'
    if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
    $par = CelularParear $porta '123456'
    if ($par.Status -ne 200 -or -not $par.Chave) { throw "pareamento falhou ($($par.Status))" }
    $chave = $par.Chave
    $seg = CelularSegurar $porta $chave $arq 120
    $ultimo = { $l = @(CelularLinhas $arq); if ($l.Count) { $l[$l.Count - 1].Estado } else { $null } }
    $titulo1 = 'Faixa 1'; $titulo2 = 'Faixa 2'

    # 7.5 Estado
    $ok75 = WaitFor { $e = & $ultimo; $a = AppDoEstado $e 'MX-teste A'; $b = AppDoEstado $e 'tocar-b'; $a -and $a.midia -and $b -and $b.midia } 6
    $e = & $ultimo
    $appA = AppDoEstado $e 'MX-teste A'; $appB = AppDoEstado $e 'tocar-b'
    $a75 = [bool]($appA -and $appA.midia.titulo -eq $titulo1 -and $appA.midia.artista -eq 'MX-teste' -and $appA.midia.tocando -and $appA.midia.ant -and $appA.midia.pp -and $appA.midia.prox -and -not $appA.soMidia)
    $b75 = [bool]($appB -and $appB.soMidia -eq $true -and $appB.chave -like 'midia:*' -and $appB.midia.titulo -eq $titulo1 -and @($appB.rotas).Count -eq 0)
    $ultimaApp = @($e.apps)[@($e.apps).Count - 1]
    $fim75 = [bool]($ultimaApp -and $ultimaApp.soMidia -eq $true)
    Resultado '7.5 Estado' ([bool]($ok75 -and $a75 -and $b75 -and $fim75)) "MX-teste A: midia.titulo='$($appA.midia.titulo)' artista='$($appA.midia.artista)' tocando=$($appA.midia.tocando) botoes=$($appA.midia.ant)/$($appA.midia.pp)/$($appA.midia.prox) soMidia=$($appA.soMidia) => $a75; tocar-b: soMidia=$($appB.soMidia) chave='$($appB.chave)' titulo='$($appB.midia.titulo)' sem rotas => $b75; ultima linha e so de midia=$fim75; apps=[$((@($e.apps) | ForEach-Object { $_.nome }) -join ', ')]"

    # 7.6 Comando (prox no MX-teste A) e, de bonus, na linha so de midia tocar-b
    $t0 = EpochMs
    $cod = CelularEnviar $porta $chave ('{"t":"midia","app":"' + $appA.chave.Replace('\', '\\') + '","acao":"prox"}')
    $next = WaitFor { (MidiaLogLer $logA) -contains 'Next' } 2
    $lat = CelularEsperar $arq $t0 { param($st) $x = AppDoEstado $st 'MX-teste A'; $x -and $x.midia.titulo -eq $titulo2 } 2
    $codB = CelularEnviar $porta $chave ('{"t":"midia","app":"' + $appB.chave + '","acao":"prox"}')
    $nextB = WaitFor { (MidiaLogLer $logB) -contains 'Next' } 2
    $latOk = ($null -ne $lat -and $lat -le 1000)
    Resultado '7.6 Comando' ([bool]($cod -eq 0 -and $next -and $latOk -and $codB -eq 0 -and $nextB)) "enviar prox ao MX-teste A: codigo=$cod; Next no log da fonte=$next; titulo novo '$titulo2' no celular em $lat ms (limite 1000, medido desde antes do envio; inclui a conexao do celular.exe)=$latOk; bonus: prox na linha so de midia tocar-b codigo=$codB, Next no log=$nextB"

    # 7.7 Recusa: midia para app que nao e de teste (celular.cs recusa sem enviar nada)
    $logAntesA = @(MidiaLogLer $logA).Count
    $real = @($e.apps) | Where-Object { $_.nome -notlike 'MX-teste*' -and $_.nome -notlike 'tocar*' } | Select-Object -First 1
    $chaveReal = if ($real) { $real.chave } else { 'chave-inexistente' }
    $nomeReal = if ($real) { $real.nome } else { '(nenhum app real: chave inexistente)' }
    $c1 = CelularEnviar $porta $chave ('{"t":"midia","app":"' + $chaveReal.Replace('\', '\\') + '","acao":"pp"}')
    $c2 = CelularEnviar $porta $chave '{"t":"midia","app":"chave-inexistente","acao":"pp"}'
    $c3 = CelularEnviar $porta $chave ('{"t":"midia","app":"' + $appA.chave.Replace('\', '\\') + '","acao":"pp"}')  # controle: MX-teste A e aceito (Pause)
    $pausaA = WaitFor { (MidiaLogLer $logA) -contains 'Pause' } 2
    $logDepoisA = @(MidiaLogLer $logA).Count
    Resultado '7.7 Recusa' ([bool]($c1 -eq 3 -and $c2 -eq 3 -and $c3 -eq 0 -and $pausaA -and $logDepoisA -eq ($logAntesA + 1))) "midia para '$nomeReal': codigo=$c1 (esperado 3); chave inexistente: codigo=$c2 (esperado 3); controle MX-teste A pp: codigo=$c3, Pause no log=$pausaA (so 1 botao novo no log: $logDepoisA contra $logAntesA)"

    # Pagina servida
    $r = Invoke-WebRequest "http://127.0.0.1:$porta/" -UseBasicParsing
    $html = [string]$r.Content
    $tam = [int]$r.RawContentLength
    $urls = @([regex]::Matches($html, 'https?://[^\s"''<>)]*') | ForEach-Object { $_.Value } | Where-Object { -not $_.StartsWith("http://127.0.0.1:$porta") })
    $temMidia = $html.Contains('soMidia') -and $html.Contains("t:'midia'")
    Resultado '7.8 Pagina servida' ([bool]($r.StatusCode -eq 200 -and $tam -lt 40960 -and $urls.Count -eq 0 -and $temMidia)) "GET /: $($r.StatusCode), $tam bytes (< 40960), URLs externas=$($urls.Count), tem a fileira de midia=$temMidia"

    $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
    "     erros no log: $($errosLog.Count)"
    $errosLog | ForEach-Object { "     $_" }
    if ($errosLog.Count -gt 0) { $script:falhas++ }
    CelularSoltar $seg $arq; $seg = $null
    $reaisDepois = AppsReais
    Resultado 'Apps reais iguais' ([bool]((@($reaisAntes) -join "`n") -eq (@($reaisDepois) -join "`n"))) "apps reais antes=$(@($reaisAntes).Count) depois=$(@($reaisDepois).Count)"
  }
  catch {
    $script:falhas++
    "FALHA (excecao): $($_.Exception.Message)"
    "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
  }
  finally {
    if ($seg) { CelularSoltar $seg $arq }
    StopPlayers
    StopAll
    if ($backupFeito) { $difFinal = @(RestoreConfig) }
  }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
  "tempo total do roteiro: $([math]::Round($tempoTotal.Elapsed.TotalSeconds, 1)) s"
  if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $falhas++ }
  ReabrirAppDoUsuario
  if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.'; exit 0 }
}

$h = $null; $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
$logA = "$script:out\midia-a.log"; $logB = "$script:out\midia-b.log"
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"
  Remove-Item $script:cfgFile -Force -ErrorAction SilentlyContinue   # configuracao padrao (o backup ja guarda a do usuario)
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  $padrao = PadraoAtual
  if (-not $padrao) { throw 'sem saida padrao: nao ha como rodar o roteiro' }
  $geralAntes = GeralAtual
  $reaisAntes = AppsReais
  $titulo1 = MidiaTexto 'Faixa 1' 'MX-teste'
  $titulo2 = MidiaTexto 'Faixa 2' 'MX-teste'

  # 7.1 Linha com midia
  $pA = StartMidia 'MX-teste A' $logA
  StartApp
  WaitFor { (LogCount 'iniciado') -ge 1 } 5 | Out-Null
  $h = AbrirJanela
  if (-not $h) { $h = AbrirJanela }
  if (-not $h) { throw 'a janela nao abriu' }
  WaitFor { $null -ne (RowElement $h 'MX-teste A') } 3 | Out-Null
  $tituloOk = WaitFor { (MidiaTitulo $h 'MX-teste A') -eq $titulo1 } 5
  $tit = MidiaTitulo $h 'MX-teste A'
  $bots = @(MidiaBotoes $h 'MX-teste A')
  $botsOk = $bots.Count -eq 3 -and (@($bots | Where-Object { $_.Habilitado }).Count -eq 3)
  $nomesOk = $bots.Count -eq 3 -and $bots[0].Nome -eq 'Faixa anterior em MX-teste A' -and $bots[1].Nome -eq 'Pausar MX-teste A' -and $bots[2].Nome -eq "Pr$([char]0x00F3)xima faixa em MX-teste A"
  $semLinhaExtra = (@(Rows $h | Where-Object { $_ -eq 'MX-teste A' }).Count -eq 1)
  Resultado '7.1 Linha com midia' ([bool]($tituloOk -and $botsOk -and $nomesOk -and $semLinhaExtra)) "MediaTitle='$tit' (esperado '$titulo1')=$tituloOk; 3 botoes ligados=$botsOk (nomes: $(($bots | ForEach-Object { $_.Nome }) -join ' / '))=$nomesOk; uma linha so de 'MX-teste A'=$semLinhaExtra; linhas=[$((Rows $h) -join ', ')]"

  # 7.2 Botoes (so na fonte de teste)
  $t0 = [Diagnostics.Stopwatch]::StartNew()
  MidiaInvoke $h 'MX-teste A' 'MediaNext'
  $next = WaitFor { (MidiaLogLer $logA) -contains 'Next' } 2
  $tit2 = WaitFor { (MidiaTitulo $h 'MX-teste A') -eq $titulo2 } 1.5
  $latNext = $t0.ElapsedMilliseconds
  $t1 = [Diagnostics.Stopwatch]::StartNew()
  MidiaInvoke $h 'MX-teste A' 'MediaPlayPause'
  $pausa = WaitFor { (MidiaLogLer $logA) -contains 'Pause' } 2
  $nomePausa = WaitFor { (MidiaBotoes $h 'MX-teste A' | Where-Object { $_.Id -eq 'MediaPlayPause' }).Nome -eq 'Tocar MX-teste A' } 1.5
  $latPausa = $t1.ElapsedMilliseconds
  $logLido = @(MidiaLogLer $logA)
  $ordem = ($logLido.Count -ge 2 -and $logLido[0] -eq 'Next' -and $logLido[1] -eq 'Pause')
  $titFim = MidiaTitulo $h 'MX-teste A'
  Resultado '7.2 Botoes' ([bool]($next -and $tit2 -and $pausa -and $nomePausa -and $ordem -and $latNext -le 1500 -and $latPausa -le 1500)) "log da fonte=[$($logLido -join ', ')] (esperado Next, Pause)=$ordem; apos Next o MediaTitle passou a '$titulo2'=$tit2 ($latNext ms incluindo o Invoke); apos Pause o botao virou 'Tocar MX-teste A'=$nomePausa ($latPausa ms); MediaTitle final='$titFim'"

  # 7.3 So de midia
  $pB = StartMidia 'tocar-b' $logB -B -SemAudio
  $apareceu = WaitFor { $null -ne (RowElement $h 'tocar-b') } 4
  $tituloB = $null
  if ($apareceu) { WaitFor { $script:tituloB = MidiaTitulo $h 'tocar-b'; $null -ne $script:tituloB } 3 | Out-Null }
  $linhas = @(Rows $h)
  $idxB = [array]::IndexOf($linhas, 'tocar-b')
  $semControles = $apareceu -and -not (RowTem $h 'tocar-b' 'SessionVolume') -and -not (RowTem $h 'tocar-b' 'SessionMute') -and -not (RowTem $h 'tocar-b' 'RouteButton')
  # no fim: nenhuma linha com volume (de som) vem depois dela
  $noFim = $false
  if ($idxB -ge 0) { $noFim = $true; for ($i = $idxB + 1; $i -lt $linhas.Count; $i++) { if (RowTem $h $linhas[$i] 'SessionVolume') { $noFim = $false } } }
  $invoc = $false
  if ($apareceu) { MidiaInvoke $h 'tocar-b' 'MediaNext'; $invoc = WaitFor { (MidiaLogLer $logB) -contains 'Next' } 2 }
  $tituloOkB = WaitFor { (MidiaTitulo $h 'tocar-b') -eq $titulo2 } 1.5
  try { if (-not $pB.HasExited) { $pB.Kill() } } catch {}
  $sumiu = WaitFor { $null -eq (RowElement $h 'tocar-b') } 2
  Resultado '7.3 So de midia' ([bool]($apareceu -and $semControles -and $noFim -and $invoc -and $tituloOkB -and $sumiu)) "linha 'tocar-b' apareceu=$apareceu (posicao $idxB de $($linhas.Count)); sem SessionVolume, SessionMute e botoes de saida=$semControles; depois de todas as linhas de som=$noFim; MediaTitle inicial='$tituloB'; Invoke em MediaNext: Next no log=$invoc e titulo passou a '$titulo2'=$tituloOkB; ao encerrar a fonte a linha sumiu em ate 2 s=$sumiu; linhas=[$($linhas -join ', ')]"

  # 7.4a Sem regressao: com a fonte de midia ligada, volume e mudo da linha MX-teste A pela janela (UIA), sem tocar em mais nada
  $volAntes = (SessionOf $pA.Id).Volume
  SetRowVolume $h 'MX-teste A' 40
  $v1 = WaitFor { [math]::Abs((SessionOf $pA.Id).Volume - 0.4) -lt 0.011 } 2
  $txtVol = RowValueText $h 'MX-teste A'
  ToggleRowMute $h 'MX-teste A'
  $m1 = WaitFor { (SessionOf $pA.Id).Mudo } 2
  $nomeMudo = WaitFor { (RowMuteName $h 'MX-teste A') -like 'Ativar som*' } 1.5
  ToggleRowMute $h 'MX-teste A'
  $m2 = WaitFor { -not (SessionOf $pA.Id).Mudo } 2
  $tituloVivo = $null -ne (MidiaTitulo $h 'MX-teste A')
  DefinirSessao $pA.Id (Fmt $volAntes) 'som' | Out-Null
  Resultado '7.4a Sem regressao' ([bool]($v1 -and $txtVol -eq '40' -and $m1 -and $nomeMudo -and $m2 -and $tituloVivo)) "com a 3a linha de midia: volume 40 gravado na sessao=$v1 (valor na linha='$txtVol'); mudo ligou=$m1 (botao virou 'Ativar som...'=$nomeMudo) e desligou=$m2; MediaTitle continua na linha=$tituloVivo"

  $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log (7.1 a 7.3): $($errosLog.Count)"
  $errosLog | ForEach-Object { "     $_" }
  if ($errosLog.Count -gt 0) { $script:falhas++ }
  $recusados = @(LogLines | Where-Object { $_ -match 'midia: comando recusado' })
  "     comandos de midia recusados pelo Windows: $($recusados.Count)"
  Cmd '--fechar'
  WaitFor { @(Windows).Count -eq 0 } 3 | Out-Null

  # Nada de real mudou
  $geralDepois = GeralAtual
  $reaisDepois = AppsReais
  Resultado 'Padrao, volume e mudo gerais e apps reais iguais' ([bool]($geralAntes -eq $geralDepois -and (@($reaisAntes) -join "`n") -eq (@($reaisDepois) -join "`n"))) "geral antes=$geralAntes depois=$geralDepois; apps reais antes=$(@($reaisAntes).Count) depois=$(@($reaisDepois).Count)"

}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  StopPlayers
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
}
Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
"processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
"tempo total do roteiro: $([math]::Round($tempoTotal.Elapsed.TotalSeconds, 1)) s"
if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $falhas++ }
ReabrirAppDoUsuario
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
