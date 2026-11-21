# Roteiro de tela da Fase 8: polimento, otimizacao e instalador.
#   -Parte a : casos 8.1 a 8.5 (janela reaproveitada, altura, icone da Loja, sem regressao, medicao).
#   -Parte b : casos 8.6 a 8.10 (otimizacao: NAudio fora e AOT; -SemMedicao pula o 8.10).
#   -Parte c : casos 8.11 e 8.12 (instalador: gerar, instalar e desinstalar em pasta de teste).
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando do app
# (--abrir-sem-foco, --fechar), UI Automation por padroes (RangeValue, Toggle, leitura) e HTTP em 127.0.0.1.
# SEGURANCA (P3): so os tocadores MX-teste (tocar, tocar-b) mudam; a padrao, o volume e o mudo gerais nao sao tocados; os apps
# reais so sao lidos (o caso 8.3 so le o icone do Spotify, se ele estiver no controle de midia). O servidor do celular so liga em
# 127.0.0.1 (--celular-local). BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro, e nada mata o roteiro
# no meio: todas as esperas sao por condicao e o finally sempre devolve tudo. Roteiro so do que a fase fez: nao roda roteiros
# anteriores; o medir.ps1 roda uma vez (caso 8.5).
param([ValidateSet('a', 'b', 'c')][string]$Parte = 'a', [switch]$SemMedicao)  # -SemMedicao pula o 8.5 (a medicao roda uma vez so)
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
function Fmt([double]$v) { $v.ToString('0.00', $inv) }
# Abre a janela sem foco (modo oculto) e espera ela existir; fecha e espera sumir.
function AbrirJanela {
  Cmd '--abrir-sem-foco'
  WaitFor { @(Windows).Count -ge 1 } 5 | Out-Null
  @(Windows) | Select-Object -First 1
}
function FecharJanela {
  Cmd '--fechar'
  WaitFor { @(Windows).Count -eq 0 } 5 | Out-Null
}
# Linhas "janela: posicao calculada x,y,l,a" do log, em ordem.
function PosLinhas {
  @(LogLines | ForEach-Object { [regex]::Match($_, 'janela: posicao calculada (-?\d+),(-?\d+),(\d+),(\d+)') } | Where-Object { $_.Success } | ForEach-Object {
      [pscustomobject]@{ X = [int]$_.Groups[1].Value; Y = [int]$_.Groups[2].Value; L = [int]$_.Groups[3].Value; A = [int]$_.Groups[4].Value } })
}
function LinhasDeTeste([IntPtr]$h) { @(Rows $h | Where-Object { $_ -like 'MX-teste*' }) }
function Mediana([double[]]$v) {
  $s = @($v | Sort-Object); if ($s.Count -eq 0) { return $null }
  if ($s.Count % 2) { $s[[int](($s.Count - 1) / 2)] } else { ($s[$s.Count / 2 - 1] + $s[$s.Count / 2]) / 2 }
}
function ApagarLog { Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue }
# Baixa o icone (HTTP em 127.0.0.1, cookie mx) e devolve os bytes (ou $null).
function BaixarIcone([int]$porta, [string]$chave, [string]$id) {
  try {
    $wc = New-Object Net.WebClient
    $wc.Headers.Add('Cookie', "mx=$chave")
    , [byte[]]$wc.DownloadData("http://127.0.0.1:$porta/icone/$id")
  } catch { $null }
}
function SaoIguais([byte[]]$a, [byte[]]$b) { $a.Length -eq $b.Length -and ([Convert]::ToBase64String($a) -eq [Convert]::ToBase64String($b)) }


# ======================================== Parte 8c ========================================
# Casos 8.11 (gerar o instalador) e 8.12 (instalar e desinstalar em pasta de teste). Seguranca: sem administrador, sem regra de
# firewall, instalacao so silenciosa e so em %TEMP%\mixer-check\instalado, sem a tarefa de iniciar com o Windows; o BackupConfig
# guarda o config.json e o valor Run; nada e morto a forca (a desinstalacao roda --sair, que fecha o app do usuario; o
# ReabrirAppDoUsuario o reabre no fim).
if ($Parte -eq 'c') {
  $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
  $destino = "$script:out\instalado"
  $chaveDes = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9D3F7A52-6B1E-4C80-A7D4-2E5B8C1F0A63}_is1'
  $atalho = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Mixer de Áudio.lnk"
  function ChaveDes { $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($chaveDes); if ($k) { $k.Close(); $true } else { $false } }
  function HashCfg { if (Test-Path $script:cfgFile) { (Get-FileHash $script:cfgFile -Algorithm SHA256).Hash } else { '(nao existe)' } }
  try {
    BackupConfig
    $backupFeito = $true
    $runAntes = RegTexto (RegLer $script:RunPath 'MixerDeAudio'); $apvAntes = RegTexto (RegLer $script:ApprovedPath 'MixerDeAudio')
    $hashAntes = HashCfg
    $antes = "pasta=$(Test-Path $destino) chave=$(ChaveDes) atalho=$(Test-Path -LiteralPath $atalho)"
    "estado inicial: $antes; Run=$runAntes; StartupApproved=$apvAntes; config sha256=$hashAntes; app do usuario rodando=$($script:usuarioRodava) ($script:exeUsuario)"
    if (Test-Path $destino) { throw "a pasta de teste $destino ja existe: apague-a antes (nao e apagada pelo roteiro)" }
    if (ChaveDes) { throw 'a chave de desinstalacao ja existe (Mixer de Audio instalado): o roteiro nao sobrepoe uma instalacao real' }

    # ---- 8.11 Gerar ----
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $saidaGerar = & powershell -NoProfile -ExecutionPolicy Bypass -File "$script:root\tools\release\gerar.ps1" 2>&1 | Out-String
    $setup = "$script:root\dist\MixerDeAudio-1.0.0-x64-setup.exe"
    $okGerar = (Test-Path $setup) -and ($saidaGerar -notmatch 'ERRO')
    $mb = if (Test-Path $setup) { [math]::Round((Get-Item $setup).Length / 1MB, 1) } else { 0 }
    Resultado '8.11 Gerar' $okGerar "setup existe=$(Test-Path $setup); tamanho=$mb MB; $([math]::Round($sw.Elapsed.TotalSeconds)) s"
    if (-not $okGerar) { $saidaGerar.Trim() }

    # ---- 8.12 Instalar e desinstalar ----
    if ($okGerar) {
      $log = "$script:out\setup.log"
      $p = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/CURRENTUSER', "/DIR=`"$destino`"", '/TASKS=""', "/LOG=`"$log`"") -PassThru -Wait
      $rcInst = $p.ExitCode
      $exeInst = "$destino\MixerDeAudio.exe"
      $versao = if (Test-Path $exeInst) { (Get-Item $exeInst).VersionInfo.FileVersion } else { '(sem exe)' }
      $nArq = if (Test-Path $destino) { @(Get-ChildItem $destino -Recurse -File).Count } else { 0 }
      $temUnins = Test-Path "$destino\unins000.exe"
      $chaveOk = ChaveDes; $atalhoOk = Test-Path -LiteralPath $atalho
      $runDurante = RegTexto (RegLer $script:RunPath 'MixerDeAudio')
      $okInst = ($rcInst -eq 0) -and $versao.StartsWith('1.0.0') -and $nArq -gt 50 -and $temUnins -and $chaveOk -and $atalhoOk -and ($runDurante -eq $runAntes)
      Resultado '8.12a Instalar' $okInst "codigo=$rcInst; arquivos=$nArq; versao do exe=$versao; unins000=$temUnins; chave HKCU=$chaveOk; atalho do Iniciar=$atalhoOk; Run igual ao de antes (sem a tarefa)=$($runDurante -eq $runAntes)"

      if (Test-Path "$destino\unins000.exe") {
        $p2 = Start-Process "$destino\unins000.exe" -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES') -PassThru -Wait
        $rcDes = $p2.ExitCode
        WaitFor { -not (Test-Path $destino) -and -not (ChaveDes) } 30 | Out-Null
        $pastaSumiu = -not (Test-Path $destino); $chaveSumiu = -not (ChaveDes); $atalhoSumiu = -not (Test-Path -LiteralPath $atalho)
        $runDepois = RegTexto (RegLer $script:RunPath 'MixerDeAudio'); $apvDepois = RegTexto (RegLer $script:ApprovedPath 'MixerDeAudio')
        $hashDepois = HashCfg
        $okDes = ($rcDes -eq 0) -and $pastaSumiu -and $chaveSumiu -and $atalhoSumiu -and ($runDepois -eq $runAntes) -and ($apvDepois -eq $apvAntes) -and ($hashDepois -eq $hashAntes)
        Resultado '8.12b Desinstalar' $okDes "codigo=$rcDes; pasta sumiu=$pastaSumiu; chave sumiu=$chaveSumiu; atalho sumiu=$atalhoSumiu; Run igual=$($runDepois -eq $runAntes); StartupApproved igual=$($apvDepois -eq $apvAntes); config.json (hash) igual=$($hashDepois -eq $hashAntes)"
      } else { Resultado '8.12b Desinstalar' $false 'sem unins000.exe' }
    }
  } finally {
    # Nada de Stop-Process: o app instalado nunca foi aberto (a instalacao e silenciosa e sem postinstall) e a desinstalacao ja fechou o do usuario por --sair.
    if ($backupFeito) { $difFinal = @(RestoreConfig) }
    "config.json e registro restaurados; diferencas=[$($difFinal -join '; ')]"
    if ((AppProcs).Count -gt 0 -and $script:usuarioRodava) { 'app do usuario ainda aberto (nao precisa reabrir)' }
    ReabrirAppDoUsuario
    'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
  }
  if ($falhas -gt 0) { exit 1 }
  exit 0
}

# ======================================== Parte 8b ========================================
# Casos 8.6 a 8.10: o NAudio saiu (interfaces proprias do Core Audio) e o build publicado e o final (AOT ou o degrau que ficou).
# Seguranca: so os tocadores MX-teste (tocar, tocar-b) mudam; rotas so deles; volume geral so +-1% e devolvido no finally;
# a saida padrao e o mudo geral nao sao tocados; servidor do celular so em 127.0.0.1 (--celular-local).
function PadraoAtual { Devices | Where-Object { $_.Padrao } | Select-Object -First 1 }
# Ordem do D7 da Fase 3: nome curto sem diferenciar maiusculas, id como desempate.
function OrdemD7($lista) {
  $arr = [System.Collections.Generic.List[object]]::new(); foreach ($d in $lista) { $arr.Add($d) }
  $arr.Sort([Comparison[object]]{ param($a, $b)
    $c = [string]::Compare($a.Curto, $b.Curto, [StringComparison]::OrdinalIgnoreCase)
    if ($c -ne 0) { $c } else { [string]::CompareOrdinal($a.Id, $b.Id) } })
  @($arr)
}
function RestaurarGeral([double]$alvo) {
  for ($i = 0; $i -lt 6; $i++) {
    $d = PadraoAtual; if (-not $d) { return }
    $dif = $alvo - $d.Volume
    if ([math]::Abs($dif) -le 0.0005) { return }
    $passo = [math]::Max(-0.02, [math]::Min(0.02, $dif))
    DefinirGeral (($d.Volume + $passo).ToString('0.000', $inv)) | Out-Null
    Start-Sleep -Milliseconds 150
  }
}
# Escolha de saida dos apps reais (so leitura): pid -> id ou 'padrao'.
function PrefsReais {
  $m = [ordered]@{}
  foreach ($s in @(Sessoes)) {
    if ($s.Pid -le 0 -or $m.Contains("$($s.Pid)")) { continue }
    $nomeProc = try { (Get-Process -Id $s.Pid -ErrorAction Stop).ProcessName } catch { '' }
    if ($nomeProc -in 'tocar', 'tocar-b' -or $s.Nome.StartsWith('MX-teste')) { continue }
    $m["$($s.Pid)"] = RotaLer $s.Pid
  }
  $m
}
function BotaoAtivo([IntPtr]$h, [string]$app) {
  $a = @(RouteButtons $h $app | Where-Object { $_.Ativo })
  if ($a.Count -eq 1) { $a[0].Nome } else { "($($a.Count) ativos)" }
}
function AbrirConfig {
  Cmd '--configuracoes-sem-foco'
  WaitFor { @(Windows).Count -ge 1 } 5 | Out-Null
  @(Windows) | Select-Object -First 1
}

if ($Parte -eq 'b') {
  $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $seg = $null
  $v0 = $null; $pidA = 0
  try {
    StopAll
    StopPlayers
    PrepararFerramentas
    BackupConfig
    $backupFeito = $true
    ApagarLog
    $padrao = PadraoAtual
    if (-not $padrao) { throw 'sem saida padrao: nao ha como rodar o roteiro' }
    $v0 = $padrao.Volume
    $outra = @(Devices) | Where-Object { -not $_.Padrao } | Select-Object -First 1
    $prefsAntes = PrefsReais
    "estado inicial: padrao='$($padrao.Curto)' volume=$(Fmt $v0) outra='$(if ($outra) { $outra.Curto } else { '(nenhuma)' })' saidas=$(@(Devices).Count) apps reais lidos=$($prefsAntes.Count)"

    # config sem o celular ligado para os casos 8.6 a 8.8 (o servidor nao e assunto deles); o BackupConfig devolve tudo no fim
    [IO.File]::WriteAllText($script:cfgFile, '{"Versao": 1}', (New-Object Text.UTF8Encoding $false))
    StartApp @('--celular-local')
    WaitFor { (LogLines | Where-Object { $_ -match 'iniciado' }).Count -ge 1 } 5 | Out-Null
    $pA = StartPlayer 'MX-teste A'
    $pB = StartPlayer 'MX-teste B' -B
    $pidA = $pA.Id
    $h = AbrirJanela
    $okLinhas = WaitFor { $r = @(Rows $h); ($r -contains 'MX-teste A') -and ($r -contains 'MX-teste B') } 5

    # ---- 8.6 Sessoes ----
    SetRowVolume $h 'MX-teste A' 30
    $okVol = WaitFor { $s = SessionOf $pA.Id; $s -and [math]::Abs($s.Volume - 0.30) -le 0.011 } 3
    ToggleRowMute $h 'MX-teste A'
    $okMudoOn = WaitFor { $s = SessionOf $pA.Id; $s -and $s.Mudo } 3
    ToggleRowMute $h 'MX-teste A'
    $okMudoOff = WaitFor { $s = SessionOf $pA.Id; $s -and -not $s.Mudo } 3
    Start-Sleep -Milliseconds 1200   # passa a janela de 1 s do filtro de eco do ajuste anterior
    $swV = [Diagnostics.Stopwatch]::StartNew()
    $rcDef = DefinirSessao $pA.Id '0.70'
    $okFora = WaitFor { (RowValueText $h 'MX-teste A') -eq '70' } 1
    $tFora = [math]::Round($swV.Elapsed.TotalSeconds, 2)
    $swB = [Diagnostics.Stopwatch]::StartNew()
    try { $pB.Kill() } catch {}
    $okSome = WaitFor { -not (@(Rows $h) -contains 'MX-teste B') } 2
    $tSome = [math]::Round($swB.Elapsed.TotalSeconds, 2)
    Resultado '8.6 Sessoes' ([bool]($okLinhas -and $okVol -and $okMudoOn -and $okMudoOff -and $rcDef -eq 0 -and $okFora -and $okSome)) "2 linhas com nome=$okLinhas; volume 30 na sessao=$okVol; mudo liga=$okMudoOn e desliga=$okMudoOff; 'sessoes definir' 70 (rc=$rcDef) apareceu na linha=$okFora (${tFora}s, limite 1); linha de tocar-b sumiu=$okSome (${tSome}s, limite 2)"

    # ---- 8.7 Geral e saidas ----
    $esperado = @(OrdemD7 (Devices))
    $botoes = @(DeviceButtons $h)
    $nomesTela = ($botoes | ForEach-Object { $_.Texto }) -join '|'
    $nomesEsp = ($esperado | ForEach-Object { $_.Curto }) -join '|'
    $ativos = @($botoes | Where-Object { $_.Ativo })
    $padraoOk = ($ativos.Count -eq 1) -and ($ativos[0].Nome -eq $padrao.Completo)
    $vSlider = [double](MasterValue $h)
    $alvo = if ($vSlider + 1 -le 100) { $vSlider + 1 } else { $vSlider - 1 }
    $okGrava = $false; $okVolta = $false; $vNovo = $null
    try {
      Start-Sleep -Milliseconds 1200
      SetMasterValue $h $alvo
      $okGrava = WaitFor { [math]::Abs((PadraoAtual).Volume * 100 - $alvo) -le 1 } 2
      $vNovo = (PadraoAtual).Volume
    } finally {
      try { SetMasterValue $h $vSlider } catch {}
      $okVolta = WaitFor { [math]::Abs((PadraoAtual).Volume * 100 - $vSlider) -le 1 } 2
      RestaurarGeral $v0
    }
    $vFim = (PadraoAtual).Volume
    Resultado '8.7 Geral e saidas' ([bool]($botoes.Count -eq $esperado.Count -and $nomesTela -eq $nomesEsp -and $padraoOk -and $okGrava -and $okVolta -and [math]::Abs($vFim - $v0) -le 0.005)) "saidas na tela=$($botoes.Count) esperado=$($esperado.Count); nomes tela='$nomesTela' esperado='$nomesEsp'; padrao destacada=$padraoOk ('$($padrao.Curto)'); +-1% gravou=$okGrava (sessoes geral=$(if ($null -ne $vNovo) { Fmt $vNovo })) voltou=$okVolta; volume final=$(Fmt $vFim) (original $(Fmt $v0))"

    # ---- 8.8 Rota ----
    if (-not $outra) { Pulado '8.8 Rota' 'so ha uma saida ativa' } else {
      ClickRoute $h 'MX-teste A' $outra.Curto
      $gravou = WaitFor { (RotaLer $pidA) -eq $outra.Id } 2
      $destacou = WaitFor { (BotaoAtivo $h 'MX-teste A') -eq "Enviar MX-teste A para $($outra.Curto)" } 1.5
      ClickRoute $h 'MX-teste A' $padrao.Curto
      $limpou = WaitFor { (RotaLer $pidA) -eq 'padrao' } 2
      $destacou2 = WaitFor { (BotaoAtivo $h 'MX-teste A') -eq "Enviar MX-teste A para $($padrao.Curto)" } 1.5
      $prefsDepois = PrefsReais
      $iguais = $true
      foreach ($k in $prefsAntes.Keys) { if ($prefsDepois.Contains($k) -and $prefsDepois[$k] -ne $prefsAntes[$k]) { $iguais = $false } }
      Resultado '8.8 Rota' ([bool]($gravou -and $destacou -and $limpou -and $destacou2 -and $iguais)) "rota-ler=outra saida=$gravou (destaque=$destacou); depois rota-ler=padrao=$limpou (destaque=$destacou2); apps reais iguais=$iguais"
    }
    FecharJanela
    Rota $pidA 'padrao' | Out-Null
    $erros = @(LogLines | Where-Object { $_ -match 'ERRO' })
    "     erros no log (8.6 a 8.8): $($erros.Count)"
    $erros | ForEach-Object { "     $_" }
    if ($erros.Count -gt 0) { $script:falhas++ }
    StopPlayers
    StopAll

    # ---- 8.9 Celular e QR ----
    $pA = StartPlayer 'MX-teste A'
    $porta = LigarCelularTeste '123456'
    if (-not $porta) { throw 'o servidor do celular nao ligou' }
    Start-Sleep -Milliseconds 500
    $h = AbrirConfig
    $okQr = $false; $qrTam = ''
    if (WaitFor { $null -ne (FindById $h 'RemoteQr') } 4) {
      Start-Sleep -Milliseconds 700
      $eQr = FindById $h 'RemoteQr'
      try { $eQr.GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 400 } catch {}
      $br = $eQr.Current.BoundingRectangle
      $okQr = ($br.Width -gt 0 -and $br.Height -gt 0)
      $qrTam = "$([int]$br.Width)x$([int]$br.Height)"
    }
    $par = CelularParear $porta '123456'
    $chave = $par.Chave
    $estado = if ($chave) { CelularEstado $porta $chave } else { $null }
    $temA = [bool](AppDoEstado $estado 'MX-teste A')
    Resultado '8.9 Celular e QR' ([bool]($okQr -and $par.Status -eq 200 -and $temA)) "RemoteQr existe com tamanho $qrTam (maior que 0)=$okQr; pareamento HTTP $($par.Status); estado com a linha MX-teste A=$temA ($(@($estado.apps).Count) apps no estado)"
    $erros = @(LogLines | Where-Object { $_ -match 'ERRO' })
    "     erros no log (8.9): $($erros.Count)"
    $erros | ForEach-Object { "     $_" }
    if ($erros.Count -gt 0) { $script:falhas++ }
    FecharJanela
    StopPlayers
    StopAll

    # ---- 8.10 Medicao (uma vez) ----
    if ($SemMedicao) { Pulado '8.10 Medicao' 'pulada por -SemMedicao' } else {
      [IO.File]::WriteAllText($script:cfgFile, '{"Versao": 1}', (New-Object Text.UTF8Encoding $false))  # servidor do celular desligado: nada escuta na rede
      ApagarLog
      $saidaMedir = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\medir.ps1" -ComSom -Ciclos 20 2>&1 | Out-String
      function Num([string]$nome) {
        $m = [regex]::Match($saidaMedir, "(?m)^\s*$([regex]::Escape($nome))\s*:\s*(-?[\d.,]+)")
        if ($m.Success) { [double]::Parse($m.Groups[1].Value.Replace(',', '.'), $inv) } else { $null }
      }
      $cpu = Num 'CPU_parado_%'; $ws = Num 'Parado10s_WsPriv_MB'; $priv = Num 'Parado10s_PrivBytes_MB'
      $b20 = Num 'Ciclo20_PrivBytes_MB'; $h20 = Num 'Handles_ciclo20'
      $ms = @(LogLines | ForEach-Object { [regex]::Match($_, 'sessoes carregadas \(.*?(\d+) ms\)') } | Where-Object { $_.Success } | ForEach-Object { [double]$_.Groups[1].Value })
      $primeira = if ($ms.Count) { $ms[0] } else { $null }
      $med = Mediana $ms
      $arqs = @(Get-ChildItem "$script:root\publicado" -Recurse -File)
      $tamMB = [math]::Round((($arqs | Measure-Object Length -Sum).Sum) / 1MB, 1)
      $ok810 = [bool]($null -ne $cpu -and $cpu -lt 0.1 -and $null -ne $ws -and $ws -le 5 -and $null -ne $primeira -and $primeira -lt 300)
      Resultado '8.10 Medicao' $ok810 "CPU parada $cpu% (limite 0,1); WS privado parado $ws MB (limite 5); bytes privados parados $priv MB; ciclo 20: bytes privados $b20 MB, handles $h20; primeira abertura $primeira ms (limite 300), mediana $med ms em $($ms.Count) aberturas; publicado\: $tamMB MB em $($arqs.Count) arquivos"
      if (-not $ok810) { $saidaMedir.Trim() }
      "     medir.ps1: $((($saidaMedir -split "`n") | Where-Object { $_ -match ':' } | ForEach-Object { $_.Trim() }) -join ' | ')"
    }
  } catch {
    $script:falhas++
    "FALHA (excecao): $($_.Exception.Message)"
  } finally {
    foreach ($p in @($script:players)) { try { if (-not $p.HasExited) { Rota $p.Id 'padrao' | Out-Null } } catch {} }
    if ($null -ne $v0) { try { RestaurarGeral $v0 } catch {} }
    StopPlayers
    StopAll
    if ($backupFeito) { $difFinal = @(RestoreConfig) }
    "config.json e registro restaurados; diferencas=[$($difFinal -join '; ')]"
    "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
    ReabrirAppDoUsuario
    'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
  }
  if ($falhas -gt 0) { exit 1 }
  exit 0
}

# ======================================== Parte 8a ========================================
$difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $seg = $null; $arq = "$script:out\celular-f8.txt"
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  ApagarLog

  # ---- 8.1 Reaproveitada e 8.2 Altura (mesma sequencia de aberturas) ----
  StartApp
  WaitFor { (LogLines | Where-Object { $_ -match 'iniciado' }).Count -ge 1 } 5 | Out-Null
  $scale = 1.0
  $workH = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea.Height
  # abertura 1 (sem tocadores de teste)
  $h = AbrirJanela
  $testes1 = @(LinhasDeTeste $h)
  $p1 = @(PosLinhas) | Select-Object -Last 1
  if ($h) { $scale = [MW]::GetDpiForWindow($h) / 96.0 }
  FecharJanela
  # abertura 2: liga o tocador B com a janela aberta (altura cresce)
  $h = AbrirJanela
  $posAntes = @(PosLinhas)
  $p2 = $posAntes | Select-Object -Last 1
  $pB = StartPlayer 'MX-teste B' -B
  $apareceu = WaitFor { (@(Rows $h) -contains 'MX-teste B') } 5
  $cresceu = WaitFor { @(PosLinhas).Count -gt $posAntes.Count } 3
  $p3 = @(PosLinhas) | Select-Object -Last 1
  FecharJanela
  # encerra B e espera a sessao sumir; liga o A com a janela fechada
  StopPlayers
  WaitFor { -not (@(Sessoes | Where-Object { $_.Nome -eq 'MX-teste B' }).Count) } 6 | Out-Null
  $pA = StartPlayer 'MX-teste A'
  $h = AbrirJanela
  $apareceuA = WaitFor { (@(Rows $h) -contains 'MX-teste A') } 5
  $testes3 = @(LinhasDeTeste $h)
  $criada = LogCount 'janela: criada'; $reaproveitada = LogCount 'janela: reaproveitada'
  $sessoesLog = @(LogLines | Where-Object { $_ -match 'sessoes carregadas' }).Count
  $ok81 = [bool]($criada -eq 1 -and $reaproveitada -eq 2 -and $sessoesLog -eq 3 -and $testes1.Count -eq 0 -and $apareceuA -and $testes3.Count -eq 1 -and $testes3[0] -eq 'MX-teste A')
  Resultado '8.1 Reaproveitada' $ok81 "log: criada=$criada reaproveitada=$reaproveitada 'sessoes carregadas'=$sessoesLog (esperado 1/2/3); linhas de teste na 1a abertura=$($testes1.Count) (esperado 0); na 3a=[$($testes3 -join ', ')] (so MX-teste A)"

  $maxPx = [math]::Min(560 * $scale, 0.7 * $workH)
  $a0 = $p2.A; $a1 = $p3.A
  $base0 = $p2.Y + $p2.A; $base1 = $p3.Y + $p3.A
  # Com muitos apps reais abertos a janela ja nasce no maximo: entao nao cresce (nem loga nova posicao) e a borda de baixo fica igual.
  $noMax = ($a0 -ge ($maxPx - 1))
  $cresceuAlt = ($a1 -gt $a0) -or $noMax
  $desejadas = @(LogLines | Where-Object { $_ -match 'janela: altura desejada' } | ForEach-Object { ($_ -replace '^.*janela: altura desejada ', '') })
  $ok82 = [bool]($apareceu -and ($cresceu -or $noMax) -and $cresceuAlt -and $a1 -le ($maxPx + 1) -and [math]::Abs($base1 - $base0) -le 1)
  Resultado '8.2 Altura' $ok82 "abertura 1: a=$($p1.A) px (escala $(Fmt $scale)); abertura 2 antes de ligar tocar-b: a=$a0, y+a=$base0; depois: a=$a1, y+a=$base1 (diferenca $([math]::Abs($base1 - $base0)) px, limite 1); linha MX-teste B apareceu=$apareceu; nova posicao no log=$cresceu; maximo=$([math]::Round($maxPx)) px (560 DIP ou 70% de $workH); ja no maximo=$noMax; medidas do conteudo no log: [$($desejadas -join ' | ')]"

  # ---- 8.4 Sem regressao (a janela da 3a abertura segue aberta; o tocador A toca) ----
  $okVol = $false; $okMudoOn = $false; $okMudoOff = $false; $texto = $null
  if ($apareceuA) {
    SetRowVolume $h 'MX-teste A' 40
    $okVol = WaitFor { $s = SessionOf $pA.Id; $s -and [math]::Abs($s.Volume - 0.40) -le 0.011 } 3
    $texto = RowValueText $h 'MX-teste A'
    ToggleRowMute $h 'MX-teste A'
    $okMudoOn = WaitFor { $s = SessionOf $pA.Id; $s -and $s.Mudo } 3
    ToggleRowMute $h 'MX-teste A'
    $okMudoOff = WaitFor { $s = SessionOf $pA.Id; $s -and -not $s.Mudo } 3
  }
  $sA = SessionOf $pA.Id
  Resultado '8.4 Sem regressao' ([bool]($okVol -and $okMudoOn -and $okMudoOff)) "volume 40 gravado na sessao=$okVol (sessao=$(if ($sA) { Fmt $sA.Volume }), texto da linha='$texto'); mudo liga=$okMudoOn e desliga=$okMudoOff apos a reabertura"
  FecharJanela

  # ---- 8.3 Icone da Loja (so leitura; celular local) ----
  StopAll
  $porta = LigarCelularTeste '123456'
  if (-not $porta) { throw 'o servidor do celular nao ligou' }
  $par = CelularParear $porta '123456'
  if ($par.Status -ne 200 -or -not $par.Chave) { throw "pareamento falhou ($($par.Status))" }
  $chave = $par.Chave
  $seg = CelularSegurar $porta $chave $arq 60
  WaitFor { @(CelularLinhas $arq).Count -ge 1 } 6 | Out-Null
  $linhas = @(CelularLinhas $arq)
  $estado = if ($linhas.Count) { $linhas[$linhas.Count - 1].Estado } else { $null }
  $spot = @($estado.apps) | Where-Object { $_.nome -like '*Spotify*' } | Select-Object -First 1
  $tocarApp = AppDoEstado $estado 'MX-teste A'
  # Verificacao do caminho novo (shell, sem System.Drawing) com apps de .exe: o tocador de teste e um app real so lido.
  $exeApps = @($tocarApp) + @(@($estado.apps) | Where-Object { $_.nome -in 'Discord', 'Steam' } | Select-Object -First 1) | Where-Object { $_ }
  $detExe = @()
  $okExe = [bool]$exeApps.Count
  foreach ($ap in $exeApps) {
    $png = BaixarIcone $porta $chave $ap.icone
    $ehPngExe = [bool]($png -and $png.Length -gt 70 -and $png[0] -eq 0x89 -and $png[1] -eq 0x50)
    # IHDR: largura e altura (big-endian) nos bytes 16 a 23
    $tam = if ($ehPngExe) { ([int]$png[18] * 256 + $png[19]) } else { 0 }
    $tamH = if ($ehPngExe) { ([int]$png[22] * 256 + $png[23]) } else { 0 }
    if (-not $ehPngExe -or $tam -ne 32 -or $tamH -ne 32) { $okExe = $false }
    $detExe += "'$($ap.nome)': $(if ($png) { $png.Length } else { 0 }) bytes, ${tam}x${tamH}"
  }
  Resultado '8.3a Icone pelo shell (.exe)' $okExe "PNG de 32x32 servido para: $($detExe -join '; ')"
  if (-not $spot) {
    Pulado '8.3 Icone da Loja' "o Spotify nao esta no controle de midia nem tem sessao de som (apps no estado: $((@($estado.apps) | ForEach-Object { $_.nome }) -join ', '))"
  } else {
    $pngS = BaixarIcone $porta $chave $spot.icone
    $pngT = if ($tocarApp) { BaixarIcone $porta $chave $tocarApp.icone } else { $null }
    $naoVazio = [bool]($pngS -and $pngS.Length -gt 0)
    $difere = [bool]($naoVazio -and $pngT -and $pngT.Length -gt 0 -and -not (SaoIguais $pngS $pngT))
    $ehPng = [bool]($naoVazio -and $pngS[0] -eq 0x89 -and $pngS[1] -eq 0x50)
    Resultado '8.3 Icone da Loja' ([bool]($naoVazio -and $ehPng -and $difere)) "icone do '$($spot.nome)': $(if ($pngS) { $pngS.Length } else { 0 }) bytes, PNG=$ehPng; icone generico de referencia (.exe do tocar) $(if ($pngT) { $pngT.Length } else { 0 }) bytes; diferentes=$difere"
  }
  CelularSoltar $seg $arq; $seg = $null
  StopAll
  StopPlayers
  WaitFor { -not (@(Sessoes | Where-Object { $_.Nome -like 'MX-teste*' }).Count) } 6 | Out-Null

  # ---- 8.5 Medicao (uma vez) ----
  if ($SemMedicao) { Pulado '8.5 Medicao' 'pulada por -SemMedicao' } else {
  ApagarLog
  $saidaMedir = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\medir.ps1" -ComSom -Ciclos 50 2>&1 | Out-String
  function Num([string]$nome) {
    $m = [regex]::Match($saidaMedir, "(?m)^\s*$([regex]::Escape($nome))\s*:\s*(-?[\d.,]+)")
    if ($m.Success) { [double]::Parse($m.Groups[1].Value.Replace(',', '.'), $inv) } else { $null }
  }
  $h20 = Num 'Handles_ciclo20'; $h50 = Num 'Handles_ciclo50'
  $b20 = Num 'Ciclo20_PrivBytes_MB'; $b50 = Num 'Ciclo50_PrivBytes_MB'
  $cpu = Num 'CPU_parado_%'
  $ms = @(LogLines | ForEach-Object { [regex]::Match($_, 'sessoes carregadas \(.*?(\d+) ms\)') } | Where-Object { $_.Success } | ForEach-Object { [double]$_.Groups[1].Value })
  $med = Mediana $ms
  $dH = $h50 - $h20; $dB = [math]::Round($b50 - $b20, 1)
  $ok85 = [bool]($null -ne $h20 -and $null -ne $h50 -and $dH -lt 15 -and $dB -lt 2 -and $null -ne $med -and $med -le 200 -and $null -ne $cpu -and $cpu -lt 0.1)
  Resultado '8.5 Medicao' $ok85 "handles ciclo 20 -> 50: $h20 -> $h50 (+$dH; limite +15; Fase 5: +61); bytes privados 20 -> 50: $b20 -> $b50 MB (+$dB; limite 2); mediana de abertura $med ms em $($ms.Count) aberturas (max $(($ms | Measure-Object -Maximum).Maximum) ms; limite 200); CPU parada $cpu% (limite 0,1)"
  if (-not $ok85) { $saidaMedir.Trim() }
  }
} finally {
  if ($seg) { CelularSoltar $seg $arq }
  StopPlayers
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  "config.json e registro restaurados; diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
