# Roteiro de tela da Fase 6: controle pelo celular.
#   -Parte a : casos 6.1 a 6.7 (nucleo de audio compartilhado e servidor).
#   -Parte b : casos 6.8 a 6.11 (pagina do celular, secao nas Configuracoes e medicao). -SemMedicao pula o 6.11 (cerca de 12 minutos).
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando
# do app, UI Automation por padroes (Invoke, Toggle, RangeValue) e o celular de teste (celular.cs).
# SEGURANCA (P3): o servidor so e ligado em 127.0.0.1 (--celular-local), entao nunca aparece o aviso do firewall. BackupConfig
# guarda o config.json e os valores MixerDeAudio do registro; RestoreConfig no finally devolve tudo e confere. O celular de teste
# so mexe nos tocadores MX-teste (recusa geral e padrao de verdade). O roteiro confere que a padrao, o volume e mudo gerais, o
# volume, o mudo e a escolha de saida dos apps reais ficaram iguais. Nao troca a saida padrao nem mexe no mudo geral.
# -SemRegressao pula o caso 6.7 (roteiro-f5 -SemMedicao, que roda f4, f3, f2 e f1; leva varios minutos).
param([ValidateSet('a', 'b')][string]$Parte = 'a', [switch]$SemRegressao, [switch]$SemMedicao)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$inv = [Globalization.CultureInfo]::InvariantCulture
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Pulado([string]$caso, [string]$motivo) { 'PULADO {0}: {1}' -f $caso, $motivo }
function AbrirJanela([string]$cmd = '--abrir-sem-foco') {
  Cmd $cmd
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  @(Windows) | Select-Object -First 1
}
function PadraoAtual { Devices | Where-Object { $_.Padrao } | Select-Object -First 1 }
function Config { if (Test-Path $script:cfgFile) { Get-Content $script:cfgFile -Raw -Encoding utf8 | ConvertFrom-Json } else { $null } }
function Prioridade { $p = MainProc; if ($p) { $p.Refresh(); "$($p.PriorityClass)" } else { '' } }
function Fmt([double]$v) { $v.ToString('0.00', $inv) }
# Apps que nao sao tocadores de teste: pid, nome, volume e mudo (so leitura) e a escolha de saida de cada um.
function AppsReais {
  @(Sessoes | Where-Object { $_.Nome -notlike 'MX-teste*' -and $_.Pid -gt 0 } | ForEach-Object {
      "{0}|{1}|{2}|{3}|{4}" -f $_.Pid, $_.Nome, (Fmt $_.Volume), $_.Mudo, (RotaLer $_.Pid) } | Sort-Object)
}
function GeralAtual { $d = PadraoAtual; "{0}|{1}|{2}" -f $d.Id, (Fmt $d.Volume), $d.Mudo }

if ($Parte -eq 'b') {
  # ---------------- Parte b: pagina do celular e secao nas Configuracoes (casos 6.8 a 6.11) ----------------
  $h = $null; $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $seg = $null; $arq = "$script:out\celular-segurar.txt"
  try {
    StopAll
    StopPlayers
    PrepararFerramentas
    BackupConfig
    $backupFeito = $true
    "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"

    # 6.8 Pagina servida
    $porta = LigarCelularTeste '123456'
    if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
    $r = Invoke-WebRequest "http://127.0.0.1:$porta/" -UseBasicParsing
    $tipo = "$($r.Headers['Content-Type'])"
    $tam = [int]$r.RawContentLength
    $html = [string]$r.Content
    $urls = @([regex]::Matches($html, 'https?://[^\s"''<>)]*') | ForEach-Object { $_.Value } | Where-Object { -not $_.StartsWith("http://127.0.0.1:$porta") })
    $semExterno = ($urls.Count -eq 0) -and ($html -notmatch '(src|href)\s*=\s*["'']?//') -and ($html -notmatch '@import')
    $final = $html.Contains('prefers-color-scheme') -and $html.Contains('/parear') -and $html.Contains('/ws') -and $html.Contains('Reconectando') -and $html.Contains('visibilitychange') -and -not $html.Contains('provis')
    Resultado '6.8 Pagina servida' ([bool]($r.StatusCode -eq 200 -and $tipo -eq 'text/html; charset=utf-8' -and $tam -lt 40960 -and $semExterno -and $final)) "GET /: $($r.StatusCode) tipo='$tipo' tamanho=$tam bytes (< 40960) sem URL externa=$semExterno ($($urls.Count) achadas); e a pagina final (tema, /parear, /ws, reconexao, visibilitychange)=$final"
    StopAll

    # 6.9 Secao nas Configuracoes
    $porta = LigarCelularTeste '123456'
    if (-not $porta) { throw 'o servidor do celular nao ligou' }
    Start-Sleep -Milliseconds 500
    $h = AbrirJanela '--configuracoes-sem-foco'
    if (-not (WaitFor { $null -ne (FindById $h 'RemoteSwitch') } 3)) { throw 'a secao Controle pelo celular nao apareceu' }
    Start-Sleep -Milliseconds 700
    $sw = FindById $h 'RemoteSwitch'
    $ligado = $sw.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On
    $eAddr = FindById $h 'RemoteAddress'; $eCode = FindById $h 'RemoteCode'; $eQr = FindById $h 'RemoteQr'; $eCnt = FindById $h 'RemoteCount'; $eNew = FindById $h 'RemoteNewCode'
    $addr = if ($eAddr) { $eAddr.Current.Name } else { '' }
    $codeUi = if ($eCode) { $eCode.Current.Name } else { '' }
    $cnt = if ($eCnt) { $eCnt.Current.Name } else { '' }
    $qrOk = $false
    try { if ($eQr) { $eQr.GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 400 } } catch {}
    if ($eQr) { $br = $eQr.Current.BoundingRectangle; $qrOk = ($br.Width -ge 150 -and $br.Height -ge 150) }
    $cfg = Config
    $addrOk = $addr -match "^http://\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}:$porta$"
    try { if ($eNew) { $eNew.GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 400 } } catch {}
    $shot = Shot $h 'f6-config'
    Resultado '6.9 Secao nas Configuracoes' ([bool]($ligado -and $addrOk -and $codeUi -eq $cfg.CodigoCelular -and $qrOk -and $cnt -like '0 celulares*' -and $null -ne $eNew)) "interruptor ligado=$ligado; RemoteAddress='$addr' (porta $porta)=$addrOk; RemoteCode igual ao do config.json=$($codeUi -eq $cfg.CodigoCelular); RemoteQr existe com tamanho >= 150=$qrOk; RemoteCount='$cnt'; RemoteNewCode existe=$($null -ne $eNew); captura=$shot"

    # 6.10 Novo codigo
    $codigoAntigo = $cfg.CodigoCelular
    $par = CelularParear $porta $codigoAntigo
    $chave = $par.Chave
    if (-not $chave) { throw 'o pareamento falhou' }
    $seg = CelularSegurar $porta $chave $arq 300
    WaitFor { (Test-Path $arq) -and ((Get-Content $arq -Encoding utf8 -ErrorAction SilentlyContinue) -contains 'PRONTO') } 5 | Out-Null
    $umNaTela = WaitFor { (FindById $h 'RemoteCount').Current.Name -eq '1 celular conectado' } 2
    InvokeById $h 'RemoteNewCode'
    $fechou = WaitFor { $seg.HasExited } 5
    $seg = $null
    $velha = CelularGet $porta '/ws' $chave
    $cfg2 = Config
    $mudou = ($cfg2.CodigoCelular -match '^\d{6}$') -and ($cfg2.CodigoCelular -ne $codigoAntigo)
    $semChaves = @($cfg2.ChavesCelular).Count -eq 0
    $zeroNaTela = WaitFor { (FindById $h 'RemoteCount').Current.Name -like '0 celulares*' } 3
    $codeNovoUi = (FindById $h 'RemoteCode').Current.Name -eq $cfg2.CodigoCelular
    $logNovo = (LogCount 'celular: novo codigo gerado') -ge 1
    Resultado '6.10 Novo codigo' ([bool]($umNaTela -and $fechou -and $velha -eq 401 -and $mudou -and $semChaves -and $zeroNaTela -and $codeNovoUi -and $logNovo)) "com o celular conectado o RemoteCount mostrou 1=$umNaTela; WebSocket do celular de teste fechado=$fechou; chave antiga em /ws=$velha (esperado 401); codigo mudou=$mudou; config.json sem chaves=$semChaves; RemoteCode na tela igual ao novo=$codeNovoUi; RemoteCount voltou a 0=$zeroNaTela; log=$logNovo"

    $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
    "     erros no log (6.8 a 6.10): $($errosLog.Count)"
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
    if ($seg) { try { $seg.Kill() } catch {} }
    StopPlayers
    StopAll
    if ($backupFeito) { $difFinal = @(RestoreConfig) }
  }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"

  # 6.11 Medicao (cada medir.ps1 guarda e devolve o config.json sozinho). Servidor desligado (base), ligado sem celular, e com celular.
  if (-not $SemMedicao) {
    function Rodar([string[]]$extra) { & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\medir.ps1" -ComSom -Ciclos 50 @extra 2>&1 | Out-String }
    function NumDe([string]$texto, [string]$nome) { $mm = [regex]::Match($texto, "(?m)^\s*$([regex]::Escape($nome))\s*:\s*([-0-9.,]+)"); if ($mm.Success) { [double]::Parse($mm.Groups[1].Value.Replace(',', '.'), $inv) } else { $null } }
    $saidaBase = Rodar @()
    $saidaServ = Rodar @('-ComServidor')
    $tempos = @(LogLines | ForEach-Object { $m = [regex]::Match($_, 'sessoes carregadas .*?(\d+) ms\)'); if ($m.Success) { [int]$m.Groups[1].Value } })
    $abMax = if ($tempos.Count) { ($tempos | Measure-Object -Maximum).Maximum } else { $null }
    $saidaCel = Rodar @('-ComCelular')
    $baseWs = NumDe $saidaBase 'Parado10s_WsPriv_MB'
    $sWs = NumDe $saidaServ 'Parado10s_WsPriv_MB'; $sCpu = NumDe $saidaServ 'CPU_parado_%'; $sCres = NumDe $saidaServ 'Crescimento5a20_WsPriv_MB'; $sCresB = NumDe $saidaServ 'Crescimento20a50_PrivBytes_MB'
    $cCpu = NumDe $saidaCel 'CPU_parado_%'; $cCresB = NumDe $saidaCel 'Crescimento20a50_PrivBytes_MB'
    $hand = "$(NumDe $saidaCel 'Handles_parado') / $(NumDe $saidaCel 'Handles_ciclo5') / $(NumDe $saidaCel 'Handles_ciclo20') / $(NumDe $saidaCel 'Handles_ciclo50')"
    $paradoOk = ($null -ne $sCpu -and $sCpu -lt 0.1) -and ($null -ne $sWs -and $null -ne $baseWs -and ($sWs - $baseWs) -le 2)
    $janelaOk = ($null -ne $sCres -and $sCres -lt 5) -and ($null -ne $sCresB -and $sCresB -lt 3) -and ($null -ne $abMax -and $abMax -lt 300)
    $celOk = ($null -ne $cCresB -and $cCresB -lt 3) -and ($null -ne $cCpu -and $cCpu -lt 0.1)
    Resultado '6.11 Medicao' ([bool]($paradoOk -and $janelaOk -and $celOk)) "parado: CPU=$sCpu% (< 0,1) WS privado parado=$sWs MB contra $baseWs MB com o servidor desligado (diferenca <= 2); janela com servidor: WS privado 5 a 20=$sCres MB (< 5) bytes privados 20 a 50=$sCresB MB (< 3) abertura maxima=$abMax ms (< 300); celular (50 conexoes): CPU=$cCpu% bytes privados 20 a 50=$cCresB MB (< 3) handles parado/5/20/50=$hand"
    foreach ($par in @(@('servidor desligado', $saidaBase), @('servidor ligado, sem celular', $saidaServ), @('com celular', $saidaCel))) {
      "     [$($par[0])]"; ($par[1] -split "`r?`n") | Where-Object { $_.Trim() } | ForEach-Object { "       $_" }
    }
  } else { Pulado '6.11 Medicao' 'parametro -SemMedicao' }

  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $falhas++ }
  if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
  exit
}

$h = $null; $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $segurar = $null; $arq = "$script:out\celular-segurar.txt"
$volumeInicialA = $null
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"
  Remove-Item $script:cfgFile -Force -ErrorAction SilentlyContinue   # configuracao padrao (o backup ja guarda a do usuario)
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  $todas = @(Devices)
  $padrao = PadraoAtual
  if (-not $padrao) { throw 'sem saida padrao: nao ha como rodar o roteiro' }
  $outras = @($todas | Where-Object { -not $_.Padrao })
  "estado inicial: padrao='$($padrao.Curto)' outras=$($outras.Count) saidas=$($todas.Count)"
  # Estado que nao pode mudar: padrao, volume e mudo gerais, e os apps reais (volume, mudo e escolha de saida)
  $geralAntes = GeralAtual
  $reaisAntes = AppsReais

  # 6.1 Desligado nao escuta
  StartApp
  Start-Sleep -Seconds 3
  $portas = @(PortasDoApp)
  $logCel = @(LogLines | Where-Object { $_ -match 'celular' }).Count
  $efic = Prioridade
  Resultado '6.1 Desligado nao escuta' ([bool]($portas.Count -eq 0 -and $logCel -eq 0 -and (AppProcs).Count -eq 1)) "portas de 8750 a 8759 abertas pelo app=$($portas.Count) (esperado 0); linhas do log com 'celular'=$logCel (esperado 0); prioridade em repouso=$efic"
  StopAll

  # 6.2 Pareamento
  $porta = LigarCelularTeste '123456'
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  Start-Sleep -Milliseconds 500
  $escuta = @(PortasDoApp)
  $soLocal = $escuta.Count -ge 1 -and (@($escuta | Where-Object { $_.LocalAddress -ne '127.0.0.1' }).Count -eq 0)
  $erros = @(1..5 | ForEach-Object { (CelularParear $porta '000000').Status })
  $sexto = CelularParear $porta '123456'   # certo, mas ja bloqueado
  $bloqLog = (LogCount 'celular: pareamento bloqueado por 60 s') -ge 1
  "     aguardando 61 s do bloqueio..."
  Start-Sleep -Seconds 61
  $certo = CelularParear $porta '123456'
  $chave = $certo.Chave
  $cfgTexto = Get-Content $script:cfgFile -Raw -Encoding utf8
  $cfg = $cfgTexto | ConvertFrom-Json
  $umHash = @($cfg.ChavesCelular).Count -eq 1 -and "$(@($cfg.ChavesCelular)[0].Hash)".Length -eq 64
  $semChave = $chave.Length -gt 0 -and -not $cfgTexto.Contains($chave)
  $logTexto = (LogLines) -join "`n"
  $semSegredoNoLog = $chave.Length -gt 0 -and -not $logTexto.Contains($chave) -and -not $logTexto.Contains('123456')
  $errosOk = (@($erros | Where-Object { $_ -eq 401 }).Count -eq 5)
  Resultado '6.2 Pareamento' ([bool]($soLocal -and $errosOk -and $sexto.Status -eq 429 -and $bloqLog -and $certo.Status -eq 200 -and $umHash -and $semChave -and $semSegredoNoLog)) "servidor em 127.0.0.1:$porta (so local=$soLocal); 5 erros=[$($erros -join ',')] 6o (certo, bloqueado)=$($sexto.Status) (esperado 429); apos 60 s o certo=$($certo.Status) com chave de $($chave.Length) caracteres; config.json com 1 hash de 64=$umHash e sem a chave em texto=$semChave; log sem codigo nem chave=$semSegredoNoLog; log de bloqueio=$bloqLog"

  # 6.3 Sem chave nao entra
  $wsSem = CelularGet $porta '/ws'
  $icoSem = CelularGet $porta '/icone/0123456789abcdef'
  $wsRuim = CelularGet $porta '/ws' 'chave-errada'
  $wsOrigem = CelularGet $porta '/ws' $chave 'http://evil.example'
  $icoRuim = CelularGet $porta '/icone/0123456789abcdef' 'chave-errada'
  $icoId = CelularGet $porta '/icone/xyz' $chave
  $pagina = CelularGet $porta '/'
  $outra404 = CelularGet $porta '/qualquer'
  Resultado '6.3 Sem chave nao entra' ([bool]($wsSem -eq 401 -and $icoSem -eq 401 -and $wsRuim -eq 401 -and $icoRuim -eq 401 -and $wsOrigem -eq 403 -and $icoId -eq 404 -and $pagina -eq 200 -and $outra404 -eq 404)) "/ws sem cookie=$wsSem /icone sem cookie=$icoSem (esperado 401); /ws com chave errada=$wsRuim /icone com chave errada=$icoRuim (401); /ws com chave certa e Origin errado=$wsOrigem (403); /icone com id invalido=$icoId (404); GET /=$pagina (200); rota desconhecida=$outra404 (404)"

  # 6.4 Estado e ao vivo
  $pA = StartPlayer 'MX-teste A'
  $volumeInicialA = (SessionOf $pA.Id).Volume
  Start-Sleep -Milliseconds 800
  $janelaFechada = @(Windows).Count -eq 0
  $est = CelularEstado $porta $chave
  $appA = AppDoEstado $est 'MX-teste A'
  $geralEst = $est.geral
  $dpad = PadraoAtual
  $saidasOk = $est -and @($est.saidas).Count -eq $todas.Count -and ((@($est.saidas | ForEach-Object { $_.nome } | Sort-Object) -join '|') -eq (@($todas | ForEach-Object { $_.Curto } | Sort-Object) -join '|'))
  $ativaOk = $est -and @($est.saidas | Where-Object { $_.ativa }).Count -eq 1 -and (@($est.saidas | Where-Object { $_.ativa })[0].id -eq $dpad.Id)
  $geralOk = $geralEst -and $geralEst.disponivel -and [math]::Abs($geralEst.volume - [math]::Round($dpad.Volume * 100)) -le 1 -and ($geralEst.mudo -eq $dpad.Mudo)
  $estadoOk = [bool]($appA -and $saidasOk -and $ativaOk -and $geralOk -and $janelaFechada)
  $keyA = if ($appA) { $appA.chave } else { '' }
  $segurar = CelularSegurar $porta $chave $arq 300
  $pronto = WaitFor { (Test-Path $arq) -and ((Get-Content $arq -Encoding utf8 -ErrorAction SilentlyContinue) -contains 'PRONTO') } 5
  # mudanca de fora (sessoes.exe) chega ao celular
  $t0 = EpochMs
  DefinirSessao $pA.Id '0.60' | Out-Null
  $latFora = CelularEsperar $arq $t0 { param($e) $a = @($e.apps) | Where-Object { $_.nome -eq 'MX-teste A' }; $a -and $a.volume -eq 60 } 2
  # mudanca pela janela chega ao celular
  $h = AbrirJanela
  WaitFor { $null -ne (RowElement $h 'MX-teste A') } 3 | Out-Null
  $t1 = EpochMs
  SetRowVolume $h 'MX-teste A' 40
  $latJanela = CelularEsperar $arq $t1 { param($e) $a = @($e.apps) | Where-Object { $_.nome -eq 'MX-teste A' }; $a -and $a.volume -eq 40 } 2
  $sessA = SessionOf $pA.Id
  $conectou = (LogCount 'celular conectado \(1 no total\)') -ge 1
  $okLat = ($null -ne $latFora -and $latFora -le 1000) -and ($null -ne $latJanela -and $latJanela -le 1000)
  Resultado '6.4 Estado e ao vivo' ([bool]($estadoOk -and $pronto -and $okLat -and $sessA.Volume -eq 0.4 -and $conectou)) "estado com janela fechada: tem MX-teste A=$([bool]$appA) saidas iguais ($($todas.Count))=$saidasOk ativa=padrao=$ativaOk geral (volume/mudo da padrao)=$geralOk; mudanca de fora chegou ao celular em $latFora ms (<= 1000); mudanca pela janela chegou em $latJanela ms (<= 1000); sessao de A=$(Fmt $sessA.Volume); log 'celular conectado (1 no total)'=$conectou"
  CelularSoltar $segurar $arq; $segurar = $null

  # 6.5 Comandos do celular (janela aberta)
  $fotoAntes = [pscustomobject]@{ Padrao = (PadraoAtual).Id; Geral = GeralAtual; Reais = (AppsReais) }
  $rec1 = CelularEnviar $porta $chave '{"t":"geral","v":10}'
  $rec2 = CelularEnviar $porta $chave '{"t":"padrao","id":"qualquer"}'
  $rec3 = CelularEnviar $porta $chave '{"t":"geralMudo","m":true}'
  $rec4 = CelularEnviar $porta $chave '{"t":"volume","app":"c:\\windows\\system32\\notepad.exe","v":1}'
  $recusas = ($rec1 -eq 3 -and $rec2 -eq 3 -and $rec3 -eq 3 -and $rec4 -eq 3)
  $cv = CelularEnviar $porta $chave ('{"t":"volume","app":' + (ConvertTo-Json $keyA -Compress) + ',"v":30}')
  $volOk = WaitFor { (SessionOf $pA.Id).Volume -eq 0.3 } 2
  $linhaOk = WaitFor { (RowVolume $h 'MX-teste A') -eq 30 } 1.5
  $cm = CelularEnviar $porta $chave ('{"t":"mudo","app":' + (ConvertTo-Json $keyA -Compress) + ',"m":true}')
  $mudoOk = WaitFor { (SessionOf $pA.Id).Mudo } 2
  $linhaMudoOk = WaitFor { (RowMuteName $h 'MX-teste A') -like 'Ativar som*' } 1.5
  $cm2 = CelularEnviar $porta $chave ('{"t":"mudo","app":' + (ConvertTo-Json $keyA -Compress) + ',"m":false}')
  $desmudoOk = WaitFor { -not (SessionOf $pA.Id).Mudo } 2
  $notaRota = ''
  $rotaOk = $true
  if ($outras.Count -ge 1) {
    $outra = $outras[0]
    $cr1 = CelularEnviar $porta $chave ('{"t":"rota","app":' + (ConvertTo-Json $keyA -Compress) + ',"id":' + (ConvertTo-Json $outra.Id -Compress) + '}')
    $r1 = WaitFor { (RotaLer $pA.Id) -eq $outra.Id } 2
    $dest1 = WaitFor { $a = @(RouteButtons $h 'MX-teste A' | Where-Object { $_.Ativo } | ForEach-Object { $_.Nome }); $a.Count -eq 1 -and $a[0] -eq "Enviar MX-teste A para $($outra.Curto)" } 2
    $cr2 = CelularEnviar $porta $chave ('{"t":"rota","app":' + (ConvertTo-Json $keyA -Compress) + ',"id":' + (ConvertTo-Json $padrao.Id -Compress) + '}')
    $r2 = WaitFor { (RotaLer $pA.Id) -eq 'padrao' } 2
    $dest2 = WaitFor { $a = @(RouteButtons $h 'MX-teste A' | Where-Object { $_.Ativo } | ForEach-Object { $_.Nome }); $a.Count -eq 1 -and $a[0] -eq "Enviar MX-teste A para $($padrao.Curto)" } 2
    $rotaOk = [bool]($cr1 -eq 0 -and $r1 -and $dest1 -and $cr2 -eq 0 -and $r2 -and $dest2)
    $notaRota = "rota para '$($outra.Curto)': gravada=$r1 destaque na janela=$dest1; de volta a padrao: apagada=$r2 destaque=$dest2"
  } else { $notaRota = 'so ha 1 saida: a parte da rota nao pode ser testada' }
  $fotoDepois = [pscustomobject]@{ Padrao = (PadraoAtual).Id; Geral = GeralAtual; Reais = (AppsReais) }
  $iguais = ($fotoAntes.Padrao -eq $fotoDepois.Padrao) -and ($fotoAntes.Geral -eq $fotoDepois.Geral) -and ((@($fotoAntes.Reais) -join "`n") -eq (@($fotoDepois.Reais) -join "`n"))
  Resultado '6.5 Comandos do celular' ([bool]($recusas -and $cv -eq 0 -and $volOk -and $linhaOk -and $cm -eq 0 -and $mudoOk -and $linhaMudoOk -and $cm2 -eq 0 -and $desmudoOk -and $rotaOk -and $iguais)) "recusas do celular de teste (geral, padrao, geralMudo, app real)=$recusas; volume 30: sessao=$volOk linha da janela=$linhaOk; mudo: sessao=$mudoOk linha=$linhaMudoOk desmudou=$desmudoOk; $notaRota; padrao, geral e apps reais iguais=$iguais"
  Cmd '--fechar'
  WaitFor { @(Windows).Count -eq 0 } 3 | Out-Null
  Start-Sleep -Seconds 3

  # 6.6 Hub e janela
  $criadosAntes = LogCount 'audio: monitores criados'
  $liberadosAntes = LogCount 'audio: monitores liberados'
  $segurar = CelularSegurar $porta $chave $arq 300
  WaitFor { (Test-Path $arq) -and ((Get-Content $arq -Encoding utf8 -ErrorAction SilentlyContinue) -contains 'PRONTO') } 5 | Out-Null
  Start-Sleep -Seconds 2
  $prioConectado = Prioridade
  $abriu = 0
  for ($i = 1; $i -le 5; $i++) {
    $hh = AbrirJanela
    if ($hh) { $abriu++ }
    Cmd '--fechar'
    WaitFor { @(Windows).Count -eq 0 } 3 | Out-Null
    Start-Sleep -Milliseconds 300
  }
  $criados = (LogCount 'audio: monitores criados') - $criadosAntes
  $liberadosDurante = (LogCount 'audio: monitores liberados') - $liberadosAntes
  $prioDurante = Prioridade
  # os monitores continuam vivos: uma mudanca de fora ainda chega ao celular
  $t2 = EpochMs
  DefinirSessao $pA.Id '0.55' | Out-Null
  $latVivo = CelularEsperar $arq $t2 { param($e) $a = @($e.apps) | Where-Object { $_.nome -eq 'MX-teste A' }; $a -and $a.volume -eq 55 } 2
  $p = MainProc; $p.Refresh(); $wsAntes = [math]::Round($p.WorkingSet64 / 1MB, 1)
  CelularSoltar $segurar $arq; $segurar = $null
  $liberou = WaitFor { (LogCount 'audio: monitores liberados') -gt $liberadosAntes } 5
  $voltouEfic = WaitFor { (Prioridade) -eq 'Idle' } 5
  Start-Sleep -Seconds 4   # o trim sai 2 s depois
  $p = MainProc; $p.Refresh(); $wsDepois = [math]::Round($p.WorkingSet64 / 1MB, 1)
  $desconLog = (LogCount 'celular desconectado \(0\)') -ge 1
  Resultado '6.6 Hub e janela' ([bool]($abriu -eq 5 -and $criados -eq 1 -and $liberadosDurante -eq 0 -and $null -ne $latVivo -and $prioConectado -ne 'Idle' -and $prioDurante -ne 'Idle' -and $liberou -and $voltouEfic -and $wsDepois -lt $wsAntes -and $desconLog)) "janela aberta $abriu/5 vezes; criacoes dos monitores=$criados (esperado 1); liberacoes durante o celular=$liberadosDurante (esperado 0); prioridade com celular=$prioConectado/$prioDurante (nao Idle); mudanca de fora ainda chega ao celular=$latVivo ms; apos desconectar: monitores liberados=$liberou modo de eficiencia de volta (Idle)=$voltouEfic conjunto de trabalho $wsAntes MB -> $wsDepois MB (trim); log 'celular desconectado (0)'=$desconLog"

  $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log (6.1 a 6.6): $($errosLog.Count)"
  $errosLog | ForEach-Object { "     $_" }
  if ($errosLog.Count -gt 0) { $script:falhas++ }

  # Encerra o app e os tocadores antes de rodar os outros roteiros
  Cmd '--fechar'
  try { if ($outras.Count -ge 1) { Rota $pA.Id 'padrao' | Out-Null } } catch {}
  if ($volumeInicialA -ne $null) { DefinirSessao $pA.Id (Fmt $volumeInicialA) 'som' | Out-Null }
  StopPlayers
  StopAll
  Remove-Item $script:cfgFile -Force -ErrorAction SilentlyContinue   # configuracao padrao para o 6.7

  # 6.7 Sem regressao (roteiro-f5 -SemMedicao, que roda f4, f3, f2 e f1)
  if (-not $SemRegressao) {
    $saidaF5 = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\roteiro-f5.ps1" -SemMedicao 2>&1 | Out-String
    $rcF5 = $LASTEXITCODE
    $linhasF5 = @(($saidaF5 -split "`r?`n") | Where-Object { $_ -match '^(OK|FALHA|PULADO|Todos|FALHAS)' })
    $okF5 = @($linhasF5 | Where-Object { $_ -match '^OK ' }).Count
    Resultado '6.7 Sem regressao' ([bool]($rcF5 -eq 0 -and $saidaF5 -match 'Todos os casos passaram' -and $saidaF5 -notmatch '(?m)^FALHA')) "roteiro-f5 -SemMedicao: codigo=$rcF5 linhas OK=$okF5"
    $linhasF5 | ForEach-Object { '     ' + ($_ -replace '(?s)(.{170}).*', '$1...') }
  } else { Pulado '6.7 Sem regressao' 'parametro -SemRegressao' }
}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  if ($segurar) { try { $segurar.Kill() } catch {} }
  try { foreach ($p in @($script:players)) { if ($p -and -not $p.HasExited -and $outras.Count -ge 1) { Rota $p.Id 'padrao' | Out-Null } } } catch {}
  StopPlayers
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
}
Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
"processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $falhas++ }
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
