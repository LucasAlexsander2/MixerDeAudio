# Roteiro de tela da Fase 3 (casos 3.1 a 3.6): saídas de áudio e volume geral.
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANÇA (P3): nunca troca a saída padrão e nunca muda o mudo geral (só lê). O volume geral recebe só o toque
# mínimo de +1% (ou -1% se estiver no máximo) e volta ao valor original, também num bloco finally.
# Parâmetro -SemMedicao pula o caso 3.6 (que leva cerca de 5 minutos).
param([switch]$SemMedicao)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$inv = [Globalization.CultureInfo]::InvariantCulture
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Fmt($v) { if ($null -eq $v) { 'nulo' } else { ([double]$v).ToString('0.000', $inv) } }
function AbrirJanela {
  Cmd '--abrir-sem-foco'
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  @(Windows) | Select-Object -First 1
}
function PadraoAtual { Devices | Where-Object { $_.Padrao } | Select-Object -First 1 }
# Ordem do D7: nome curto sem diferenciar maiúsculas, id como desempate (comparação ordinal, como o app).
function OrdemD7($lista) {
  $arr = [System.Collections.Generic.List[object]]::new(); foreach ($d in $lista) { $arr.Add($d) }
  $arr.Sort([Comparison[object]]{ param($a, $b)
    $c = [string]::Compare($a.Curto, $b.Curto, [StringComparison]::OrdinalIgnoreCase)
    if ($c -ne 0) { $c } else { [string]::CompareOrdinal($a.Id, $b.Id) } })
  @($arr)
}
# Devolve o volume geral ao valor original em passos de até 0,02 (a trava do sessoes.exe), sem tocar no mudo.
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

$v0 = $null; $m0 = $null; $id0 = $null
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  $p0 = PadraoAtual
  if (-not $p0) { throw 'sem saida padrao: nao ha como rodar o roteiro' }
  $v0 = $p0.Volume; $m0 = $p0.Mudo; $id0 = $p0.Id
  "estado inicial: padrao='$($p0.Curto)' volume=$(Fmt $v0) mudo=$m0 saidas=$(@(Devices).Count)"

  StartApp
  Start-Sleep -Seconds 2
  $h = AbrirJanela

  # 3.1 Saídas
  $esperado = @(OrdemD7 (Devices))
  $botoes = @(DeviceButtons $h)
  $nomesTela = ($botoes | ForEach-Object { $_.Texto }) -join '|'
  $nomesEsp = ($esperado | ForEach-Object { $_.Curto }) -join '|'
  $completosTela = ($botoes | ForEach-Object { $_.Nome }) -join '|'
  $completosEsp = ($esperado | ForEach-Object { $_.Completo }) -join '|'
  $ativos = @($botoes | Where-Object { $_.Ativo })
  $ativoOk = ($ativos.Count -eq 1) -and ($ativos[0].Nome -eq $p0.Completo)
  $m = [regex]::Matches((LogLines | Out-String), 'saidas=(\d+), (\d+) ms')
  $saidasLog = if ($m.Count -gt 0) { [int]$m[$m.Count - 1].Groups[1].Value } else { -1 }
  $ms = if ($m.Count -gt 0) { [int]$m[$m.Count - 1].Groups[2].Value } else { -1 }
  $shot = Shot $h 'f3-janela'
  Resultado '3.1 Saidas' ([bool]($botoes.Count -eq $esperado.Count -and $nomesTela -eq $nomesEsp -and $completosTela -eq $completosEsp -and $ativoOk -and $saidasLog -eq $esperado.Count)) "botoes=$($botoes.Count) esperado=$($esperado.Count) nomes tela='$nomesTela' esperado='$nomesEsp' nomes completos iguais=$($completosTela -eq $completosEsp) ativo unico=$ativoOk ('$($p0.Curto)') log saidas=$saidasLog tempo de abertura=$ms ms (esperado < 300) captura=$shot"

  # 3.2 Volume geral lido
  $vTela = MasterValue $h
  $mudoTela = MasterMuted $h
  Resultado '3.2 Volume geral lido' ([bool]($null -ne $vTela -and [math]::Abs($vTela - [math]::Round($v0 * 100)) -le 1 -and $mudoTela -eq $m0)) "slider=$vTela texto='$(MasterText $h)' esperado=$([math]::Round($v0 * 100)) (+-1) mudo tela=$mudoTela mudo Windows=$m0"

  # 3.3 Volume geral gravado (toque mínimo)
  $vSlider = [double](MasterValue $h)
  $alvoSlider = if ($vSlider + 1 -le 100) { $vSlider + 1 } else { $vSlider - 1 }
  SetMasterValue $h $alvoSlider
  $okGrava = WaitFor { $d = PadraoAtual; [math]::Abs($d.Volume * 100 - $alvoSlider) -le 1 } 2
  $vNovo = (PadraoAtual).Volume
  SetMasterValue $h $vSlider
  $okVolta = WaitFor { [math]::Abs((PadraoAtual).Volume * 100 - $vSlider) -le 1 } 2
  RestaurarGeral $v0
  $vFim = (PadraoAtual).Volume
  Resultado '3.3 Volume geral gravado' ([bool]($okGrava -and $okVolta -and [math]::Abs($vFim - $v0) -le 0.005)) "original=$(Fmt $v0) pedido=$alvoSlider Windows=$(Fmt $vNovo) gravou=$okGrava voltou=$okVolta final=$(Fmt $vFim)"

  # 3.4 Mudança de fora
  Start-Sleep -Milliseconds 1300   # deixa passar a janela de 1 s do filtro de eco (o 3.3 gravou há pouco)
  $delta = if ($v0 + 0.01 -le 1.0) { 0.01 } else { -0.01 }
  $alvoFora = [math]::Round($v0 + $delta, 3)
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $rc = DefinirGeral ($alvoFora.ToString('0.000', $inv))
  $viu = WaitFor { [math]::Abs((MasterValue $h) - [math]::Round($alvoFora * 100)) -le 0.5 } 1.5
  $tempo = [math]::Round($sw.Elapsed.TotalSeconds, 2)
  Start-Sleep -Milliseconds 500
  $semEco = [math]::Abs((PadraoAtual).Volume - $alvoFora) -le 0.006   # a tela não gravou de volta
  RestaurarGeral $v0
  $voltou = WaitFor { [math]::Abs((MasterValue $h) - [math]::Round($v0 * 100)) -le 0.5 } 1.5
  $vFim = (PadraoAtual).Volume
  Resultado '3.4 Mudanca de fora' ([bool]($rc -eq 0 -and $viu -and $semEco -and $voltou -and [math]::Abs($vFim - $v0) -le 0.005)) "sessoes geral=$(Fmt $alvoFora) rc=$rc slider acompanhou em ${tempo}s=$viu sem gravar de volta=$semEco slider voltou=$voltou final=$(Fmt $vFim) (original $(Fmt $v0))"

  $erros = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log: $($erros.Count)"
  $erros | ForEach-Object { "     $_" }
  if ($erros.Count -gt 0) { $script:falhas++ }

  # Encerrar o app antes do 3.5 (o roteiro da Fase 2 reinicia o app e apaga o log).
  StopPlayers
  StopAll

  # 3.5 Sem regressão
  $saidaF2 = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\roteiro-f2.ps1" 2>&1 | Out-String
  $linhasF2 = @(($saidaF2 -split "`r?`n") | Where-Object { $_ -match '^(OK|FALHA|Todos|FALHAS)' })
  $okF2 = @($linhasF2 | Where-Object { $_ -match '^OK    2\.[1-8] ' }).Count
  $f1Ok = ($saidaF2 -match 'roteiro-f1 saida:.*OK    1\.1.*OK    1\.2.*OK    1\.3.*OK    1\.5')
  Resultado '3.5 Sem regressao' ([bool]($okF2 -eq 8 -and $f1Ok -and $saidaF2 -match 'Todos os casos passaram')) "roteiro-f2: casos OK=$okF2 de 8, roteiro-f1 dentro do 2.8 OK=$f1Ok"
  $linhasF2 | ForEach-Object { '     ' + ($_ -replace '(?s)(.{170}).*', '$1...') }
}
finally {
  # O volume geral volta ao valor original em qualquer caso (inclusive erro).
  if ($null -ne $v0) {
    try { RestaurarGeral $v0 } catch { "AVISO: nao foi possivel restaurar o volume geral: $($_.Exception.Message)" }
  }
  StopPlayers
  StopAll
}

# Conferência final do estado do som (antes da medição, que não mexe no volume).
$pf = PadraoAtual
$somOk = $pf -and $pf.Id -eq $id0 -and [math]::Abs($pf.Volume - $v0) -le 0.005 -and $pf.Mudo -eq $m0
Resultado 'Final Som igual ao do inicio' ([bool]$somOk) "padrao igual=$($pf.Id -eq $id0) volume $(Fmt $v0) -> $(Fmt $pf.Volume) mudo $m0 -> $($pf.Mudo)"

# 3.6 Medição (50 ciclos, com dois tocadores de teste)
if (-not $SemMedicao) {
  $saidaM = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\medir.ps1" -ComSom -Ciclos 50 2>&1 | Out-String
  function Num([string]$nome) { $mm = [regex]::Match($saidaM, "(?m)^\s*$([regex]::Escape($nome))\s*:\s*([-0-9.,]+)"); if ($mm.Success) { [double]::Parse($mm.Groups[1].Value.Replace(',', '.'), $inv) } else { $null } }
  $cpu = Num 'CPU_parado_%'; $cres = Num 'Crescimento5a20_WsPriv_MB'; $cresB = Num 'Crescimento20a50_PrivBytes_MB'
  $okM = ($null -ne $cpu -and $cpu -lt 0.1) -and ($null -ne $cres -and $cres -lt 5) -and ($null -ne $cresB -and $cresB -lt 3)
  Resultado '3.6 Medicao' ([bool]$okM) "CPU parada=$cpu% (< 0,1) WS privado 5 a 20=$cres MB (< 5) bytes privados 20 a 50=$cresB MB (< 3)"
  ($saidaM -split "`r?`n") | Where-Object { $_.Trim() } | ForEach-Object { "     $_" }
}

"processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
