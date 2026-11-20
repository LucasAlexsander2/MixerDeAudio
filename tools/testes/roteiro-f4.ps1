# Roteiro de tela da Fase 4 (casos 4.1 a 4.6): saída por app.
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANÇA (P3): só os tocadores de teste (MX-teste, processos tocar e tocar-b) têm a saída trocada; o sessoes.exe
# e o ClickRoute recusam qualquer outro processo ou linha. Os apps reais só são lidos, e a escolha deles tem que ser
# igual antes e depois. Nunca troca a saída padrão e nunca mexe no volume geral nem no mudo. No finally, a escolha
# dos tocadores ainda vivos é apagada (Rota <pid> padrao) antes de encerrá-los.
# Parâmetro -SemMedicao pula o caso 4.6 (cerca de 5 minutos).
param([switch]$SemMedicao)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$inv = [Globalization.CultureInfo]::InvariantCulture
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Pulado([string]$caso, [string]$motivo) { 'PULADO {0}: {1}' -f $caso, $motivo }
function AbrirJanela {
  Cmd '--abrir-sem-foco'
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  @(Windows) | Select-Object -First 1
}
function PadraoAtual { Devices | Where-Object { $_.Padrao } | Select-Object -First 1 }
# Escolha de saída de todos os apps reais (tudo que não é tocador de teste), só leitura: pid -> id ou 'padrao'.
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
function NaSaida([int]$procId, [string]$idSaida) { @(Sessoes | Where-Object { $_.Pid -eq $procId -and $_.SaidaId -eq $idSaida }).Count -gt 0 }
# Nome do botão ativo da linha (ou '' se não houver exatamente um).
function BotaoAtivo([IntPtr]$h, [string]$app) {
  $a = @(RouteButtons $h $app | Where-Object { $_.Ativo })
  if ($a.Count -eq 1) { $a[0].Nome } else { "($($a.Count) ativos)" }
}

$prefsAntes = $null; $ultimaLeitura = @{}; $pidA = 0
$padrao = $null; $outra = $null; $h = $null
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  $todas = @(Devices)
  $padrao = PadraoAtual
  if (-not $padrao) { throw 'sem saida padrao: nao ha como rodar o roteiro' }
  $outra = $todas | Where-Object { -not $_.Padrao } | Select-Object -First 1
  $prefsAntes = PrefsReais
  "estado inicial: padrao='$($padrao.Curto)' outra='$(if ($outra) { $outra.Curto } else { '(nenhuma)' })' saidas=$($todas.Count) apps reais lidos=$($prefsAntes.Count)"

  $pA = StartPlayer 'MX-teste A'
  $pidA = $pA.Id
  StartApp
  Start-Sleep -Seconds 2
  $h = AbrirJanela

  # 4.1 Botões nas linhas
  $linhas = @(Rows $h)
  $erros41 = New-Object System.Collections.ArrayList
  foreach ($l in $linhas) {
    $bs = @(RouteButtons $h $l)
    if ($l -eq 'Sons do sistema') { if ($bs.Count -ne 0) { [void]$erros41.Add("'$l' tem $($bs.Count) botoes") } ; continue }
    if ($todas.Count -ge 2) {
      if ($bs.Count -ne $todas.Count) { [void]$erros41.Add("'$l' tem $($bs.Count) botoes (esperado $($todas.Count))") }
      $ativos = @($bs | Where-Object { $_.Ativo })
      if ($ativos.Count -ne 1) { [void]$erros41.Add("'$l' tem $($ativos.Count) botoes ativos") }
    } elseif ($bs.Count -ne 0) { [void]$erros41.Add("'$l' tem botoes com so 1 saida") }
  }
  $btA = @(RouteButtons $h 'MX-teste A')
  $nomesEsp = @($todas | ForEach-Object { "Enviar MX-teste A para $($_.Curto)" } | Sort-Object)
  $nomesA = @($btA | ForEach-Object { $_.Nome } | Sort-Object)
  $nomesOk = ($todas.Count -lt 2) -or (($nomesEsp -join '|') -eq ($nomesA -join '|'))
  $ativoA = BotaoAtivo $h 'MX-teste A'
  $ativoOk = ($todas.Count -lt 2) -or ($ativoA -eq "Enviar MX-teste A para $($padrao.Curto)")
  $shot = Shot $h 'f4-janela'
  Resultado '4.1 Botoes nas linhas' ([bool]($erros41.Count -eq 0 -and $nomesOk -and $ativoOk -and $linhas -contains 'MX-teste A')) "linhas=$($linhas.Count) botoes de A=$($btA.Count) (saidas=$($todas.Count)) nomes de A corretos=$nomesOk ativo de A='$ativoA' (esperado a padrao '$($padrao.Curto)') erros=[$($erros41 -join '; ')] captura=$shot"
  $btA | ForEach-Object { "     $($_.Nome) ativo=$($_.Ativo)" }

  if (-not $outra) {
    $motivo = 'nao ha uma saida que nao seja a padrao (so 1 saida ativa)'
    Pulado '4.2 Enviar para outra saida' $motivo
    Pulado '4.3 Reabrir mantem' $motivo
    Pulado '4.4 Voltar a seguir a padrao' $motivo
  } else {
    # 4.2 Enviar para outra saída
    $antesLog = LogCount 'saida de MX-teste A trocada para'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    ClickRoute $h 'MX-teste A' $outra.Curto
    $gravou = WaitFor { (RotaLer $pidA) -eq $outra.Id } 2
    $tGrava = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    $destacou = WaitFor { (BotaoAtivo $h 'MX-teste A') -eq "Enviar MX-teste A para $($outra.Curto)" } 1.5
    $tDestaque = [math]::Round($sw.Elapsed.TotalSeconds, 2)
    $logOk = (LogCount 'saida de MX-teste A trocada para') -gt $antesLog
    $ultimaLeitura['A'] = RotaLer $pidA
    $naHora = WaitFor { NaSaida $pidA $outra.Id } 3
    $caso = ''
    if ($naHora) { $caso = 'a sessao mudou de saida NA HORA (em ate 3 s), sem reiniciar o tocador' }
    else {
      # Só depois de reiniciar o som: encerra o tocador (apagando antes nada: a escolha é do .exe e deve valer no novo processo).
      try { $pA.Kill() } catch {}
      WaitFor { $pA.HasExited } 3 | Out-Null
      Start-Sleep -Milliseconds 500
      $pA = StartPlayer 'MX-teste A'
      $pidA = $pA.Id
      $apareceu = WaitFor { NaSaida $pidA $outra.Id } 5
      $caso = "a sessao NAO mudou na hora; depois de REINICIAR o tocador (novo pid $pidA) a sessao nova apareceu na saida escolhida=$apareceu"
      $naHora = $apareceu
      $gravou = $gravou -and ((RotaLer $pidA) -eq $outra.Id)
    }
    $prefsDurante = PrefsReais
    $padraoDepois = PadraoAtual
    $iguais = $true
    foreach ($k in $prefsAntes.Keys) { if ($prefsDurante.Contains($k) -and $prefsDurante[$k] -ne $prefsAntes[$k]) { $iguais = $false } }
    Resultado '4.2 Enviar para outra saida' ([bool]($gravou -and $destacou -and $logOk -and $naHora -and $iguais -and $padraoDepois.Id -eq $padrao.Id)) "rota-ler=outra saida=$gravou (${tGrava}s) destaque mudou=$destacou (${tDestaque}s) log=$logOk padrao igual=$($padraoDepois.Id -eq $padrao.Id) apps reais iguais=$iguais; $caso"

    # 4.3 Reabrir mantém
    Cmd '--fechar'
    WaitFor { @(Windows).Count -eq 0 } 3 | Out-Null
    $h = AbrirJanela
    $okLinha = WaitFor { $null -ne (RowElement $h 'MX-teste A') } 3
    Start-Sleep -Milliseconds 400
    $ativoA = BotaoAtivo $h 'MX-teste A'
    Resultado '4.3 Reabrir mantem' ([bool]($okLinha -and $ativoA -eq "Enviar MX-teste A para $($outra.Curto)")) "destaque de A='$ativoA' (esperado '$($outra.Curto)')"

    # 4.4 Voltar a seguir a padrão
    $antesLog = LogCount 'volta a seguir a padrao'
    ClickRoute $h 'MX-teste A' $padrao.Curto
    $limpou = WaitFor { (RotaLer $pidA) -eq 'padrao' } 2
    $destacou = WaitFor { (BotaoAtivo $h 'MX-teste A') -eq "Enviar MX-teste A para $($padrao.Curto)" } 1.5
    $logOk = (LogCount 'volta a seguir a padrao') -gt $antesLog
    $ultimaLeitura['A'] = RotaLer $pidA
    $voltou = WaitFor { NaSaida $pidA $padrao.Id } 3
    Resultado '4.4 Voltar a seguir a padrao' ([bool]($limpou -and $destacou -and $logOk)) "rota-ler=padrao: $limpou destaque na padrao=$destacou log=$logOk (a sessao voltou para a padrao em ate 3 s: $voltou; informativo)"
  }

  $erros = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log: $($erros.Count)"
  $erros | ForEach-Object { "     $_" }
  if ($erros.Count -gt 0) { $script:falhas++ }
}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
}
finally {
  # Ordem: apagar a escolha dos tocadores ainda vivos, depois encerrar os tocadores e o app.
  foreach ($p in @($script:players)) {
    try {
      if (-not $p.HasExited) {
        Rota $p.Id 'padrao' | Out-Null
        $ultimaLeitura["pid$($p.Id)"] = RotaLer $p.Id
      }
    } catch { "AVISO: nao foi possivel apagar a escolha do tocador $($p.Id): $($_.Exception.Message)" }
  }
  StopPlayers
  StopAll
}
$leituraFinal = @($ultimaLeitura.GetEnumerator() | Where-Object { $_.Key -like 'pid*' } | ForEach-Object { $_.Value })
Resultado 'Final Escolha dos tocadores apagada' ([bool](@($leituraFinal | Where-Object { $_ -ne 'padrao' }).Count -eq 0)) "ultima leitura dos tocadores vivos: [$($leituraFinal -join ', ')] (esperado padrao)"

# 4.5 Sem regressão (roda f3 sem medição, que roda f2, que roda f1)
$saidaF3 = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\roteiro-f3.ps1" -SemMedicao 2>&1 | Out-String
$rcF3 = $LASTEXITCODE
$linhasF3 = @(($saidaF3 -split "`r?`n") | Where-Object { $_ -match '^(OK|FALHA|Todos|FALHAS)' })
$okF3 = @($linhasF3 | Where-Object { $_ -match '^OK    3\.[1-5] ' }).Count
Resultado '4.5 Sem regressao' ([bool]($rcF3 -eq 0 -and $okF3 -eq 5 -and $saidaF3 -match 'Todos os casos passaram' -and $saidaF3 -notmatch '(?m)^FALHA')) "roteiro-f3 -SemMedicao: codigo=$rcF3 casos 3.1 a 3.5 OK=$okF3 de 5"
$linhasF3 | ForEach-Object { '     ' + ($_ -replace '(?s)(.{170}).*', '$1...') }

# 4.6 Medição (50 ciclos, com dois tocadores de teste)
if (-not $SemMedicao) {
  $saidaM = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\medir.ps1" -ComSom -Ciclos 50 2>&1 | Out-String
  function Num([string]$nome) { $mm = [regex]::Match($saidaM, "(?m)^\s*$([regex]::Escape($nome))\s*:\s*([-0-9.,]+)"); if ($mm.Success) { [double]::Parse($mm.Groups[1].Value.Replace(',', '.'), $inv) } else { $null } }
  $cpu = Num 'CPU_parado_%'; $cres = Num 'Crescimento5a20_WsPriv_MB'; $cresB = Num 'Crescimento20a50_PrivBytes_MB'
  $hand = "$(Num 'Handles_parado') / $(Num 'Handles_ciclo5') / $(Num 'Handles_ciclo20') / $(Num 'Handles_ciclo50')"
  $okM = ($null -ne $cpu -and $cpu -lt 0.1) -and ($null -ne $cres -and $cres -lt 5) -and ($null -ne $cresB -and $cresB -lt 3)
  Resultado '4.6 Medicao' ([bool]$okM) "CPU parada=$cpu% (< 0,1) WS privado 5 a 20=$cres MB (< 5) bytes privados 20 a 50=$cresB MB (< 3) handles parado/5/20/50=$hand"
  ($saidaM -split "`r?`n") | Where-Object { $_.Trim() } | ForEach-Object { "     $_" }
} else { Pulado '4.6 Medicao' 'parametro -SemMedicao' }

# Conferência final: a escolha dos apps reais é a mesma de antes, e nenhum processo sobrou.
if ($null -ne $prefsAntes) {
  $prefsDepois = PrefsReais
  $comparados = 0; $difs = New-Object System.Collections.ArrayList
  foreach ($k in $prefsAntes.Keys) {
    if ($prefsDepois.Contains($k)) { $comparados++; if ($prefsDepois[$k] -ne $prefsAntes[$k]) { [void]$difs.Add("pid $k : $($prefsAntes[$k]) -> $($prefsDepois[$k])") } }
  }
  Resultado 'Final Escolha dos apps reais igual' ([bool]($difs.Count -eq 0)) "apps comparados=$comparados (antes=$($prefsAntes.Count)) diferencas=[$($difs -join '; ')]"
}
$pf = PadraoAtual
Resultado 'Final Saida padrao igual' ([bool]($pf -and $padrao -and $pf.Id -eq $padrao.Id)) "padrao '$(if ($pf) { $pf.Curto })'"
"processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $falhas++ }
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
