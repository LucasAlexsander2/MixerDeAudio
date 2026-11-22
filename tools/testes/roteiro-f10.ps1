# Roteiro de tela da Fase 10: tela do PC fluida e em tela cheia (so o canal /tela/ws; o resto fica nos testes unitarios).
#   Casos 10.1 a 10.4: canal e controle de fluxo, troca de qualidade, regras (401, 409, 403, segundo canal) e leveza.
#   Caso 10.5 (parte 10b): /sessao e a porta fixa ao religar o app com o celular de teste reconectando (celular.exe reconectar).
#   -Caso 10.5 roda SO o 10.5 (sem repetir 10.1 a 10.4); sem -Caso rodam todos.
#   -SemMedicao pula o 10.4 (cerca de 2,5 min; o resto leva cerca de 1 min).
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando do app
# (--configuracoes-sem-foco), UI Automation por padroes (Toggle, leitura) e o celular de teste (celular.cs) em 127.0.0.1.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone). Nenhuma tecla, clique ou acao real no PC.
# A captura da tela e real, so de leitura, e a imagem nunca e gravada em disco (o celular.exe so le cabecalho e dimensoes do JPEG).
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao, com prazo folgado).
param([switch]$SemMedicao, [string]$Caso = '')
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
$script:inicioRoteiro = Get-Date
$inv = [Globalization.CultureInfo]::InvariantCulture
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Config { if (Test-Path $script:cfgFile) { Get-Content $script:cfgFile -Raw -Encoding utf8 | ConvertFrom-Json } else { $null } }
function AbrirJanela([string]$cmd = '--abrir-sem-foco') {
  Cmd $cmd
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  @(Windows) | Select-Object -First 1
}
function Pronto([string]$arquivo) { WaitFor { (Test-Path $arquivo) -and ((Get-Content $arquivo -Encoding utf8 -ErrorAction SilentlyContinue) -contains 'PRONTO') } 5 }
function MemApp { $p = MainProc; $p.Refresh(); [pscustomobject]@{ PrivMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); Handles = $p.HandleCount } }
# Tamanho esperado do quadro: largura pedida cortada em [160, maximo da qualidade] e na largura da tela; altura na mesma proporcao.
function Esperado([int]$sw, [int]$sh, [int]$pedida, [int]$max) {
  $w = [math]::Min([math]::Min([math]::Max($pedida, 160), $max), $sw)
  @($w, [math]::Max(1, [int][math]::Round($sh * $w / $sw, [MidpointRounding]::AwayFromZero)))
}
# Ultima linha "tela encerrada (N quadros, M iguais, X ms por quadro, q)" do log: Quadros, Iguais, Ms, Q (ou $null).
function LinhaEncerrada([int]$indice = -1) {
  $l = @(LogLines | ForEach-Object { [regex]::Match($_, 'celular: tela encerrada \((\d+) quadros, (\d+) iguais, (\d+) ms por quadro, (\w+)\)') } | Where-Object { $_.Success })
  if ($l.Count -eq 0) { return $null }
  $m = if ($indice -lt 0) { $l[$l.Count - 1] } else { $l[$indice] }
  [pscustomobject]@{ Quadros = [int]$m.Groups[1].Value; Iguais = [int]$m.Groups[2].Value; Ms = [int]$m.Groups[3].Value; Q = $m.Groups[4].Value }
}

$h = $null; $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $seg = $null; $arq = "$script:out\f10-segurar.txt"; $rec = $null; $arqRec = "$script:out\f10-reconectar.txt"; $bg = $null; $arqBg = "$script:out\f10-tela-bg.txt"
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"

  $porta = LigarFerramentas @('tela')
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  AssertModoTestePc
  $pa = CelularParear $porta '123456'
  $pb = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or $pb.Status -ne 200 -or -not $pa.Chave -or -not $pb.Chave -or $pa.Chave -eq $pb.Chave) { throw "o pareamento das duas chaves falhou ($($pa.Status), $($pb.Status))" }
  $origem = "http://127.0.0.1:$porta"

  # o celular A fica conectado (sessao de controle) durante o roteiro inteiro: so ele pode abrir o canal da tela
  $seg = CelularSegurar $porta $pa.Chave $arq 900
  $pronto = Pronto $arq
  WaitFor { @(CelularLinhas $arq).Count -ge 1 } 3 | Out-Null
  $est = @(CelularLinhas $arq)[-1].Estado
  $mons = @($est.pc.monitores)
  $m0 = $mons | Where-Object { $_.i -eq 0 } | Select-Object -First 1
  if (-not ($pronto -and $mons.Count -ge 1 -and $null -ne $m0 -and $m0.w -gt 0)) { throw "o estado nao trouxe pc.monitores (monitores=$($mons.Count))" }
  $m1 = $mons | Where-Object { $_.i -eq 1 } | Select-Object -First 1
  "     telas: $($mons.Count); principal $($m0.w)x$($m0.h)$(if ($m1) { "; segunda $($m1.w)x$($m1.h)" })"

  if ($Caso -ne '10.5') {
  # 10.1 Canal e fluxo: 10 s com "ok" a cada quadro, depois 10 s sem mandar "ok"
  $nEnc0 = LogCount 'celular: tela encerrada'
  $r1 = CelularTela $porta $pa.Chave 'normal:0:1280:10:ok;normal:0:1280:10:semok'
  $enc1 = WaitFor { (LogCount 'celular: tela encerrada') -gt $nEnc0 } 15
  $le1 = LinhaEncerrada
  $p1 = @($r1.Passos | Where-Object { $_.I -eq 0 }) | Select-Object -First 1
  $p2 = @($r1.Passos | Where-Object { $_.I -eq 1 }) | Select-Object -First 1
  $esp1 = Esperado ([int]$m0.w) ([int]$m0.h) 1280 1280
  $okDim1 = [bool]($p1 -and $p1.Quadros -ge 1 -and $p1.Jpeg -and $p1.W -eq $esp1[0] -and $p1.H -eq $esp1[1])
  $qps1 = if ($p1 -and $p1.Seg -gt 0) { [math]::Round($p1.Quadros / $p1.Seg, 2) } else { -1 }
  $okQps = [bool]($p1 -and $p1.Quadros -le [math]::Floor(8 * $p1.Seg) + 1)
  $okSemOk = [bool]($p2 -and $p2.Quadros -le 1)
  $okLog1 = [bool]($enc1 -and $le1 -and $le1.Q -eq 'normal' -and $le1.Quadros -ge ($p1.Quadros + $p2.Quadros) -and $le1.Ms -ge 0)
  Resultado '10.1 Canal e fluxo' ([bool]($r1.Fim -and $null -eq $r1.Fechou -and $okDim1 -and $okQps -and $okSemOk -and $okLog1)) "normal w 1280 por 10 s com ok: $($p1.Quadros) quadros ($qps1 qps; maximo 8), JPEG=$($p1.Jpeg), $($p1.W)x$($p1.H) (esperado $($esp1[0])x$($esp1[1]))=$okDim1, media $($p1.Bytes) bytes; 10 s sem ok: $($p2.Quadros) quadros (maximo 1)=$okSemOk; log 'tela encerrada': $($le1.Quadros) quadros, $($le1.Iguais) iguais, $($le1.Ms) ms por quadro, qualidade $($le1.Q)=$okLog1"

  # 10.2 Troca de qualidade no mesmo canal (buffers refeitos no meio): alta, eco e, se houver, a segunda tela
  $nEnc0 = LogCount 'celular: tela encerrada'
  $r2 = CelularTela $porta $pa.Chave 'alta:0:3000:4:ok;eco:0:3000:4:ok;normal:1:1280:4:ok'
  $enc2 = WaitFor { (LogCount 'celular: tela encerrada') -gt $nEnc0 } 15
  $a = @($r2.Passos | Where-Object { $_.I -eq 0 }) | Select-Object -First 1
  $e = @($r2.Passos | Where-Object { $_.I -eq 1 }) | Select-Object -First 1
  $t = @($r2.Passos | Where-Object { $_.I -eq 2 }) | Select-Object -First 1
  $espA = Esperado ([int]$m0.w) ([int]$m0.h) 3000 1920
  $espE = Esperado ([int]$m0.w) ([int]$m0.h) 3000 960
  $okA = [bool]($a -and $a.Jpeg -and $a.W -eq $espA[0] -and $a.H -eq $espA[1])
  $okE = [bool]($e -and $e.Jpeg -and $e.W -eq $espE[0] -and $e.H -eq $espE[1])
  if ($m1) {
    $espT = Esperado ([int]$m1.w) ([int]$m1.h) 1280 1280
    $okT = [bool]($t -and $t.Jpeg -and $t.W -eq $espT[0] -and $t.H -eq $espT[1] -and $null -eq $r2.Fechou)
    $txtT = "segunda tela (mon 1): $($t.W)x$($t.H) (esperado $($espT[0])x$($espT[1]))=$okT"
  } else {
    $okT = [bool]($r2.Fechou -eq 4004)   # so ha uma tela: pedir a tela 2 derruba o canal com 4004
    $txtT = "so ha 1 tela: pedir mon 1 fechou o canal com $($r2.Fechou) (esperado 4004)=$okT"
  }
  Resultado '10.2 Troca de qualidade' ([bool]($okA -and $okE -and $okT -and $enc2)) "alta w 3000: $($a.W)x$($a.H) (esperado $($espA[0])x$($espA[1]))=$okA; eco w 3000: $($e.W)x$($e.H) (esperado $($espE[0])x$($espE[1]))=$okE; $txtT; 'tela encerrada' registrada=$enc2"

  # 10.3 Regras do canal: 401, 409, 403 (grupo desligado e Origin errado) e segundo canal recusado
  $g401 = CelularGet $porta '/tela/ws' '-' $origem
  $g409 = CelularGet $porta '/tela/ws' $pb.Chave $origem
  $gOri = CelularGet $porta '/tela/ws' $pa.Chave 'http://outro.exemplo'
  $h = AbrirJanela '--configuracoes-sem-foco'
  if (-not (WaitFor { $null -ne (FindById $h 'ToolScreen') } 3)) { throw 'o interruptor ToolScreen nao apareceu' }
  ToggleFerramenta $h 'ToolScreen'
  WaitFor { @((Config).FerramentasCelular) -notcontains 'tela' } 3 | Out-Null
  $g403 = CelularGet $porta '/tela/ws' $pa.Chave $origem
  ToggleFerramenta $h 'ToolScreen'
  $volta = WaitFor { @((Config).FerramentasCelular) -contains 'tela' } 3
  # segundo canal enquanto o primeiro esta aberto
  $nComp0 = LogCount 'celular: tela compartilhada'; $nEnc0 = LogCount 'celular: tela encerrada'
  $bg = CelularTelaBg $porta $pa.Chave 'normal:0:960:8:ok' $arqBg
  $abriu = WaitFor { (LogCount 'celular: tela compartilhada') -gt $nComp0 } 5
  $rec = CelularTela $porta $pa.Chave 'normal:0:640:2:ok'
  $g503 = CelularGet $porta '/tela/ws' $pa.Chave $origem
  WaitFor { $bg.HasExited } 40 | Out-Null
  $rb = CelularTelaArquivo $arqBg $bg
  $bg = $null
  $pb1 = @($rb.Passos) | Select-Object -First 1
  $okSeg = [bool]($abriu -and $null -ne $rec.Recusado -and @($rec.Passos).Count -eq 0 -and $g503 -eq 503 -and $rb.Fim -and $null -eq $rb.Fechou -and $pb1 -and $pb1.Quadros -ge 1)
  $nComp = (LogCount 'celular: tela compartilhada') - $nComp0
  Resultado '10.3 Regras do canal' ([bool]($g401 -eq 401 -and $g409 -eq 409 -and $g403 -eq 403 -and $gOri -eq 403 -and $volta -and $okSeg -and $nComp -eq 1)) "sem cookie=$g401 (401); chave de outro celular=$g409 (409); ToolScreen desligado=$g403 (403) e religado=$volta; Origin diferente do Host=$gOri (403); segundo canal com o primeiro aberto: recusado=$($null -ne $rec.Recusado) ('$($rec.Recusado)'), GET /tela/ws=$g503 (503), o primeiro continuou: $($pb1.Quadros) quadros e terminou sem fechamento=$($rb.Fim -and $null -eq $rb.Fechou); 'tela compartilhada' x$nComp (esperado 1)"
  $enc3 = WaitFor { (LogCount 'celular: tela encerrada') -gt $nEnc0 } 15

  # 10.4 Leveza: 30 s em cada qualidade (com "ok"), depois 60 s parado, com a janela de Configuracoes fechada
  if ($SemMedicao) {
    "     10.4 pulado (-SemMedicao)"
  } else {
    Cmd '--fechar'
    WaitFor { @(Windows).Count -eq 0 } 3 | Out-Null
    Start-Sleep -Seconds 5
    $p = MainProc
    $mAntes = MemApp
    $medidas = @{}
    foreach ($q in 'eco', 'normal', 'alta') {
      $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalSeconds; $r0 = Get-Date
      $bgq = CelularTelaBg $porta $pa.Chave "${q}:0:1920:30:ok" $arqBg
      $pico = 0
      while (-not $bgq.HasExited) { Start-Sleep -Milliseconds 1000; $pico = [math]::Max($pico, (MemApp).PrivMB) }   # pico de bytes privados durante a rodada
      $p.Refresh(); $c1 = $p.TotalProcessorTime.TotalSeconds; $r1d = Get-Date
      $rq = CelularTelaArquivo $arqBg $bgq
      $secs = ($r1d - $r0).TotalSeconds
      $pq = @($rq.Passos) | Select-Object -First 1
      $medidas[$q] = [pscustomobject]@{ Cpu = [math]::Round(($c1 - $c0) / $secs * 100, 2); Quadros = $(if ($pq) { $pq.Quadros } else { 0 }); Qps = $(if ($pq) { [math]::Round($pq.Quadros / $pq.Seg, 1) } else { 0 }); Bytes = $(if ($pq) { $pq.Bytes } else { 0 }); Ok = [bool]($rq.Fim -and $pq -and $pq.Jpeg); PicoMB = $pico }
      Start-Sleep -Seconds 1
    }
    $mFim = MemApp
    Start-Sleep -Seconds 60
    $mDepois = MemApp
    $difMB = [math]::Round($mDepois.PrivMB - $mAntes.PrivMB, 1)
    $difH = $mDepois.Handles - $mAntes.Handles
    $metas = @{ eco = 3; normal = 15; alta = 30 }
    $txt = ($medidas.Keys | Sort-Object { @('eco', 'normal', 'alta').IndexOf($_) } | ForEach-Object { "$_ $($medidas[$_].Cpu)% de um nucleo (meta < $($metas[$_])%; $($medidas[$_].Quadros) quadros, $($medidas[$_].Qps) qps, media $($medidas[$_].Bytes) bytes; pico de $($medidas[$_].PicoMB) MB privados)" }) -join '; '
    $okCpu = (@(@('eco', 'normal', 'alta') | Where-Object { $medidas[$_].Cpu -ge $metas[$_] }).Count -eq 0)
    $okRuns = (@($medidas.Values | Where-Object { -not $_.Ok -or $_.Quadros -lt 1 }).Count -eq 0)
    Resultado '10.4 Leveza' ([bool]($okRuns -and $okCpu -and $difMB -le 5 -and $difH -le 10)) "CPU media durante 30 s: $txt; bytes privados antes=$($mAntes.PrivMB) MB, no fim das tres rodadas=$($mFim.PrivMB) MB, 60 s depois de parar=$($mDepois.PrivMB) MB (diferenca $difMB MB; maximo 5); handles antes=$($mAntes.Handles), depois=$($mDepois.Handles) (diferenca $difH; maximo 10); todas as rodadas com JPEG=$okRuns; metas de CPU atingidas=$okCpu"
  }

  }

  # 10.5 Celular lembrado (10b): /sessao e porta fixa ao religar o app logo depois de pará-lo
  if ($Caso -eq '' -or $Caso -eq '10.5') {
    # o celular A de antes sai; o celular "lembrado" entra com a mesma chave e so reconecta (nunca chama /parear)
    if ($seg) { CelularSoltar $seg $arq; $seg = $null }
    Remove-Item $arqRec, "$arqRec.parar", "$arqRec.err" -Force -ErrorAction SilentlyContinue
    $rec = Start-Process (CelularExe) -ArgumentList @('reconectar', "$porta", $pa.Chave, '120', "`"$arqRec.parar`"") -RedirectStandardOutput $arqRec -RedirectStandardError "$arqRec.err" -PassThru -WindowStyle Hidden
    $contaConectou = { @(Get-Content $arqRec -Encoding utf8 -ErrorAction SilentlyContinue | Where-Object { $_ -like 'CONECTOU*' }).Count }
    $conectou1 = WaitFor { (& $contaConectou) -ge 1 } 10
    $s204 = CelularGet $porta '/sessao' $pa.Chave
    $s204b = CelularGet $porta '/sessao' $pb.Chave   # outro celular pareado, com o A conectado: segue 204 (sem 409)
    $s401a = CelularGet $porta '/sessao'
    $s401b = CelularGet $porta '/sessao' 'chave-que-ninguem-pareou'
    $portaAntes = CelularPorta
    $ligou0 = LogCount 'celular: servidor ligado em'
    $pareou0 = LogCount 'celular: pareado'
    $chaves0 = @((Config).ChavesCelular).Count
    # para o app de teste e religa logo em seguida, sem restaurar o config (reproduz o TIME_WAIT da porta)
    StopAll
    $cronometro = [Diagnostics.Stopwatch]::StartNew()
    StartApp @('--celular-local', '--teste-pc')
    $religou = WaitFor { (LogCount 'celular: servidor ligado em') -gt $ligou0 } 25
    $esperaMs = $cronometro.ElapsedMilliseconds
    $portaDepois = CelularPorta
    $ocupada = LogCount 'celular: porta \d+ ocupada'
    $conectou2 = WaitFor { (& $contaConectou) -ge 2 } 30
    $linhasRec = @(Get-Content $arqRec -Encoding utf8 -ErrorAction SilentlyContinue)
    $semPareamento = ((@($linhasRec | Where-Object { $_ -eq 'PAREAR' }).Count -eq 0) -and ((LogCount 'celular: pareado') -eq $pareou0) -and (@((Config).ChavesCelular).Count -eq $chaves0))
    $portaCfg = (Config).PortaCelular
    $ok105 = [bool]($conectou1 -and $s204 -eq 204 -and $s204b -eq 204 -and $s401a -eq 401 -and $s401b -eq 401 -and $religou -and $portaAntes -and $portaDepois -eq $portaAntes -and $conectou2 -and $semPareamento)
    Resultado '10.5 Celular lembrado' $ok105 "/sessao: chave boa=$s204 (204), outro celular com o A conectado=$s204b (204), sem cookie=$s401a (401), chave desconhecida=$s401b (401); porta antes=$portaAntes, depois=$portaDepois (iguais=$($portaDepois -eq $portaAntes)); servidor ligado em $esperaMs ms depois de religar (linhas 'porta ocupada' no log: $ocupada); PortaCelular no config=$portaCfg; celular de teste reconectou com a mesma chave=$conectou2, sem pareamento=$semPareamento (linhas do celular: $((($linhasRec | ForEach-Object { ($_ -split "`t")[0] }) -join ' ')))"
    # porta ocupada de verdade por 3 s (o TIME_WAIT nem sempre aparece): o app espera e liga na MESMA porta
    StopAll
    $ocup = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Loopback), $portaAntes
    $ocup.ExclusiveAddressUse = $true
    $ocup.Start()
    $ligou1 = LogCount 'celular: servidor ligado em'
    $cron2 = [Diagnostics.Stopwatch]::StartNew()
    StartApp @('--celular-local', '--teste-pc')
    Start-Sleep -Seconds 3
    $ocup.Stop()
    $religou2 = WaitFor { (LogCount 'celular: servidor ligado em') -gt $ligou1 } 20
    $espera2 = $cron2.ElapsedMilliseconds
    $portaOcup = CelularPorta
    $ocupada2 = LogCount 'celular: porta \d+ ocupada'
    $conectou3 = WaitFor { (& $contaConectou) -ge 3 } 30
    $ok105b = [bool]($religou2 -and $portaOcup -eq $portaAntes -and $ocupada2 -ge 1 -and $espera2 -ge 2500 -and $espera2 -lt 12000 -and $conectou3)
    Resultado '10.5b Porta ocupada por 3 s' $ok105b "ligou=$religou2 em $espera2 ms (esperado entre 3 e 12 s) na porta $portaOcup (antes $portaAntes); linhas 'porta ocupada'=$ocupada2; celular reconectou de novo=$conectou3"
    if ($Caso -eq '10.5') { Cmd '--fechar' }
  }

  $semDisco = (@(Get-ChildItem $script:out -Recurse -Include *.jpg, *.jpeg -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $script:inicioRoteiro }).Count -eq 0)
  "     nenhuma imagem gravada em disco=$semDisco"
  if (-not $semDisco) { $script:falhas++ }
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
  # nunca mata o celular de teste: espera o canal em segundo plano terminar sozinho (prazo folgado)
  if ($bg) { try { $bg.WaitForExit(60000) | Out-Null } catch {} }
  if ($seg) { try { CelularSoltar $seg $arq } catch {} }
  if ($rec) { try { New-Item -ItemType File -Force "$arqRec.parar" | Out-Null; $rec.WaitForExit(15000) | Out-Null } catch {} }
  Remove-Item $arqRec, "$arqRec.parar", "$arqRec.err" -Force -ErrorAction SilentlyContinue
  Remove-Item $arqBg, "$arqBg.err" -Force -ErrorAction SilentlyContinue
  StopPlayers
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
