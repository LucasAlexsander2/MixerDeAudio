# Roteiro de tela da Fase 5 (casos 5.1 a 5.9): configuracoes, saidas favoritas, tema e iniciar com o Windows.
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando
# do app, UI Automation por padroes (Invoke, Toggle, SelectionItem) e capturas com PrintWindow.
# SEGURANCA (P3): BackupConfig guarda o config.json e os valores MixerDeAudio de HKCU\...\Run e de
# HKCU\...\Explorer\StartupApproved\Run (inclusive "nao existia"); RestoreConfig no finally devolve tudo e confere.
# So o valor MixerDeAudio e escrito ou apagado. Nao troca a saida padrao, nao mexe em volume nem em mudo, nao troca a saida de apps.
# Parametro -SemMedicao pula o caso 5.9 (cerca de 5 minutos).
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
function AbrirJanela([string]$cmd = '--abrir-sem-foco') {
  Cmd $cmd
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  @(Windows) | Select-Object -First 1
}
function PadraoAtual { Devices | Where-Object { $_.Padrao } | Select-Object -First 1 }
function Config { if (Test-Path $script:cfgFile) { Get-Content $script:cfgFile -Raw -Encoding utf8 | ConvertFrom-Json } else { $null } }
# Textos (nome curto) dos botoes do topo, na ordem em que aparecem.
function Topo([IntPtr]$h) { @(DeviceButtons $h | ForEach-Object { $_.Texto }) }
function NomesRota([IntPtr]$h, [string]$app) { @(RouteButtons $h $app | ForEach-Object { $_.Nome }) }
function Cabe([string[]]$a, [string[]]$b) { (($a | Sort-Object) -join '|') -eq (($b | Sort-Object) -join '|') }

$h = $null; $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"
  # Parte da configuracao padrao (o backup ja guarda a do usuario)
  Remove-Item $script:cfgFile -Force -ErrorAction SilentlyContinue
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  $todas = @(Devices)
  $padrao = PadraoAtual
  if (-not $padrao) { throw 'sem saida padrao: nao ha como rodar o roteiro' }
  $outras = @($todas | Where-Object { -not $_.Padrao })
  if ($outras.Count -lt 1) { throw 'so ha 1 saida ativa: os casos 5.2 e 5.3 precisam de pelo menos 2' }
  $outra = $outras[0]
  $terceira = if ($outras.Count -ge 2) { $outras[1] } else { $null }
  "estado inicial: padrao='$($padrao.Curto)' outra='$($outra.Curto)' terceira='$(if ($terceira) { $terceira.Curto } else { '(nenhuma)' })' saidas=$($todas.Count)"

  $pA = StartPlayer 'MX-teste A'
  StartApp
  Start-Sleep -Seconds 2
  $h = AbrirJanela
  WaitFor { $null -ne (RowElement $h 'MX-teste A') } 3 | Out-Null

  # 5.1 Pagina de configuracoes
  $regLigado = StartupRegLigado
  OpenSettings $h
  $checks = @(FavoriteChecks $h)
  $nomesOk = Cabe @($checks | ForEach-Object { $_.Nome }) @($todas | ForEach-Object { $_.Curto })
  $temaSel = ThemeSelected $h
  $sw = StartupSwitchState $h
  $sl = FindById $h 'SessionList'
  $escondida = (-not $sl) -or $sl.Current.IsOffscreen
  $shot = Shot $h 'f5-config'
  Resultado '5.1 Pagina de configuracoes' ([bool]($nomesOk -and $checks.Count -eq $todas.Count -and $temaSel -eq 'auto' -and $sw -eq $regLigado -and $escondida)) "FavoriteCheck=$($checks.Count) (saidas=$($todas.Count)) nomes iguais=$nomesOk tema marcado='$temaSel' (esperado auto) StartupSwitch=$sw (registro=$regLigado) SessionList escondida=$escondida captura=$shot"

  # 5.2 Favoritos
  ToggleFavorite $h $padrao.Curto
  ToggleFavorite $h $outra.Curto
  $nota42 = ''
  if ($terceira) {
    ToggleFavorite $h $terceira.Curto
    $c3 = @(FavoriteChecks $h)
    $marcadas3 = @($c3 | Where-Object { $_.Marcado }).Count
    $naoMarcadas = @($c3 | Where-Object { -not $_.Marcado })
    $desabOk = (@($naoMarcadas | Where-Object { $_.Habilitado }).Count -eq 0)
    $nota42 = "com $marcadas3 marcadas as outras desabilitadas=$desabOk (nao marcadas=$($naoMarcadas.Count); " + $(if ($todas.Count -lt 4) { 'so ha 3 saidas: nao existe a 4a para testar' } else { 'a 4a testada' }) + ')'
    ToggleFavorite $h $terceira.Curto
    $c2 = @(FavoriteChecks $h)
    $reabilitou = (@($c2 | Where-Object { -not $_.Habilitado }).Count -eq 0)
    $nota42 += " apos desmarcar a 3a, todas habilitadas=$reabilitou"
  } else { $desabOk = $true; $reabilitou = $true; $nota42 = 'so ha 2 saidas: 3a e 4a nao existem' }
  $cfg = Config
  $cfgOk = $cfg -and (Cabe @($cfg.Favoritos) @($padrao.Id, $outra.Id))
  SettingsBack $h
  $topo = Topo $h
  $topoOk = (Cabe $topo @($padrao.Curto, $outra.Curto)) -and $topo.Count -eq 2
  $rotaA = NomesRota $h 'MX-teste A'
  $rotaOk = (Cabe $rotaA @("Enviar MX-teste A para $($padrao.Curto)", "Enviar MX-teste A para $($outra.Curto)")) -and $rotaA.Count -eq 2
  $logOk = (LogCount 'favoritos alterados \(2\)') -ge 1
  Resultado '5.2 Favoritos' ([bool]($desabOk -and $reabilitou -and $cfgOk -and $topoOk -and $rotaOk -and $logOk)) "$nota42; config.json tem os 2 ids=$cfgOk; topo=[$($topo -join ', ')] ok=$topoOk; botoes de MX-teste A=[$($rotaA -join ' / ')] ok=$rotaOk; log 'favoritos alterados (2)'=$logOk"

  # 5.3 Ativa fora dos favoritos
  OpenSettings $h
  ToggleFavorite $h $padrao.Curto
  $cfg = Config
  $cfgOk = $cfg -and (Cabe @($cfg.Favoritos) @($outra.Id)) -and @($cfg.Favoritos).Count -eq 1
  SettingsBack $h
  $btn = @(DeviceButtons $h)
  $topo = @($btn | ForEach-Object { $_.Texto })
  $ordemOk = $topo.Count -eq 2 -and $topo[0] -eq $outra.Curto -and $topo[1] -eq $padrao.Curto
  $destaqueOk = $btn.Count -eq 2 -and (-not $btn[0].Ativo) -and $btn[1].Ativo
  $rotaA = NomesRota $h 'MX-teste A'
  $rotaOk = (Cabe $rotaA @("Enviar MX-teste A para $($padrao.Curto)", "Enviar MX-teste A para $($outra.Curto)")) -and $rotaA.Count -eq 2
  $ativoA = @(RouteButtons $h 'MX-teste A' | Where-Object { $_.Ativo } | ForEach-Object { $_.Nome })
  $ativoAOk = $ativoA.Count -eq 1 -and $ativoA[0] -eq "Enviar MX-teste A para $($padrao.Curto)"
  $logOk = (LogCount 'favoritos alterados \(1\)') -ge 1
  Resultado '5.3 Ativa fora dos favoritos' ([bool]($cfgOk -and $ordemOk -and $destaqueOk -and $rotaOk -and $ativoAOk -and $logOk)) "config.json so com a favorita=$cfgOk; topo=[$($topo -join ', ')] (favorita primeiro, padrao no fim)=$ordemOk padrao destacada=$destaqueOk; botoes de MX-teste A=[$($rotaA -join ' / ')] ok=$rotaOk ativo='$($ativoA -join ',')' ok=$ativoAOk; log=$logOk"

  # 5.4 Tema
  OpenSettings $h
  SelectTheme $h 'claro'
  $shotC = Shot $h 'f5-tema-claro'
  $brC = BrilhoMedio $shotC
  $cfgC = (Config).Tema
  SelectTheme $h 'escuro'
  $shotE = Shot $h 'f5-tema-escuro'
  $brE = BrilhoMedio $shotE
  $cfgE = (Config).Tema
  $logT = (LogCount 'tema alterado para escuro') -ge 1 -and (LogCount 'tema alterado para claro') -ge 1
  $mica = ($brC -gt 150 -and $brE -lt 80)
  Resultado '5.4 Tema' ([bool]($brC -gt 150 -and $brE -lt 80 -and $cfgC -eq 'claro' -and $cfgE -eq 'escuro' -and $logT)) "brilho medio claro=$brC (> 150) escuro=$brE (< 80); a Mica (fundo) acompanhou o tema=$mica; config.json tema apos claro='$cfgC' apos escuro='$cfgE'; log=$logT; capturas=$shotC e $shotE"

  # 5.5 Persistencia
  Cmd '--sair'
  $saiu = WaitFor { (AppProcs).Count -eq 0 } 5
  StartApp
  $leu = WaitFor { (LogCount 'config carregada \(favoritos=1, tema=escuro\)') -ge 1 } 5
  $h = AbrirJanela
  $topo = Topo $h
  $topoOk = $topo.Count -eq 2 -and $topo[0] -eq $outra.Curto -and $topo[1] -eq $padrao.Curto
  OpenSettings $h
  $marc = @(FavoriteChecks $h | Where-Object { $_.Marcado } | ForEach-Object { $_.Nome })
  $marcOk = $marc.Count -eq 1 -and $marc[0] -eq $outra.Curto
  $temaSel = ThemeSelected $h
  $shotP = Shot $h 'f5-persistencia'
  $brP = BrilhoMedio $shotP
  Resultado '5.5 Persistencia' ([bool]($saiu -and $leu -and $topoOk -and $marcOk -and $temaSel -eq 'escuro' -and $brP -lt 80)) "saiu=$saiu log 'config carregada (favoritos=1, tema=escuro)'=$leu; topo=[$($topo -join ', ')] ok=$topoOk; favorita marcada=[$($marc -join ', ')]; tema marcado='$temaSel'; brilho apos reiniciar=$brP (< 80)"

  # 5.6 Iniciar com o Windows (so o valor MixerDeAudio da Run e escrito ou apagado)
  SettingsBack $h
  RegEscrever $script:RunPath 'MixerDeAudio' ([pscustomobject]@{ Existe = $false; Tipo = $null; Valor = $null })  # estado de partida conhecido: desligado
  $outrosAntes = RegOutros $script:RunPath
  OpenSettings $h
  $sw0 = StartupSwitchState $h
  ToggleStartup $h
  $run1 = RegLer $script:RunPath 'MixerDeAudio'
  $esperado = '"' + $script:exe + '"'
  $valorOk = $run1.Existe -and $run1.Valor -ieq $esperado
  $sw1 = StartupSwitchState $h
  $outros1 = (RegOutros $script:RunPath) -eq $outrosAntes
  ToggleStartup $h
  $run2 = RegLer $script:RunPath 'MixerDeAudio'
  $sw2 = StartupSwitchState $h
  $outros2 = (RegOutros $script:RunPath) -eq $outrosAntes
  $logL = (LogCount 'iniciar com o Windows ligado') -ge 1 -and (LogCount 'iniciar com o Windows desligado') -ge 1
  Resultado '5.6 Iniciar com o Windows' ([bool](($sw0 -eq $false) -and $valorOk -and $sw1 -and $outros1 -and (-not $run2.Existe) -and ($sw2 -eq $false) -and $outros2 -and $logL)) "inicio: desligado=$($sw0 -eq $false); 1a vez: valor=$(RegTexto $run1) switch ligado=$sw1 valor igual ao esperado=$valorOk; 2a vez: valor existe=$($run2.Existe) switch ligado=$sw2; outros valores da Run iguais=$($outros1 -and $outros2); log=$logL"

  # 5.7 Comando --configuracoes-sem-foco
  Cmd '--fechar'
  $fechou = WaitFor { @(Windows).Count -eq 0 } 3
  $fgAntes = [MW]::GetForegroundWindow()
  $h = AbrirJanela '--configuracoes-sem-foco'
  $fgDepois = [MW]::GetForegroundWindow()
  $semFoco = ($fgDepois -ne $h)  # o usuario pode trocar de janela por conta propria; o que importa e o app nao tomar o primeiro plano
  $naPagina = ($null -ne (FindById $h 'SettingsBack')) -and ($null -eq (FindById $h 'DeviceList'))
  Resultado '5.7 Comando --configuracoes' ([bool]($fechou -and $h -and $naPagina -and $semFoco)) "fechou antes=$fechou janela abriu=$([bool]$h) ja na pagina de configuracoes=$naPagina janela do app NAO e a do primeiro plano=$semFoco (primeiro plano antes=$fgAntes depois=$fgDepois)"

  $erros = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log (5.1 a 5.7): $($erros.Count)"
  $erros | ForEach-Object { "     $_" }
  if ($erros.Count -gt 0) { $script:falhas++ }

  # Encerra o app e os tocadores antes de rodar os outros roteiros
  Cmd '--fechar'
  StopPlayers
  StopAll
  Remove-Item $script:cfgFile -Force -ErrorAction SilentlyContinue   # configuracao padrao para o 5.8 e o 5.9

  # 5.8 Sem regressao (roteiro-f4 -SemMedicao, que roda f3, f2 e f1)
  $saidaF4 = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\roteiro-f4.ps1" -SemMedicao 2>&1 | Out-String
  $rcF4 = $LASTEXITCODE
  $linhasF4 = @(($saidaF4 -split "`r?`n") | Where-Object { $_ -match '^(OK|FALHA|PULADO|Todos|FALHAS)' })
  $okF4 = @($linhasF4 | Where-Object { $_ -match '^OK ' }).Count
  Resultado '5.8 Sem regressao' ([bool]($rcF4 -eq 0 -and $saidaF4 -match 'Todos os casos passaram' -and $saidaF4 -notmatch '(?m)^FALHA')) "roteiro-f4 -SemMedicao: codigo=$rcF4 linhas OK=$okF4"
  $linhasF4 | ForEach-Object { '     ' + ($_ -replace '(?s)(.{170}).*', '$1...') }

  # 5.9 Medicao (50 ciclos, com dois tocadores de teste, configuracao padrao)
  if (-not $SemMedicao) {
    Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue
    $saidaM = & powershell -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot\medir.ps1" -ComSom -Ciclos 50 2>&1 | Out-String
    function Num([string]$nome) { $mm = [regex]::Match($saidaM, "(?m)^\s*$([regex]::Escape($nome))\s*:\s*([-0-9.,]+)"); if ($mm.Success) { [double]::Parse($mm.Groups[1].Value.Replace(',', '.'), $inv) } else { $null } }
    $cpu = Num 'CPU_parado_%'; $cres = Num 'Crescimento5a20_WsPriv_MB'; $cresB = Num 'Crescimento20a50_PrivBytes_MB'
    $tempos = @(LogLines | ForEach-Object { $m = [regex]::Match($_, 'sessoes carregadas .*?(\d+) ms\)'); if ($m.Success) { [int]$m.Groups[1].Value } })
    $abMax = if ($tempos.Count) { ($tempos | Measure-Object -Maximum).Maximum } else { $null }
    $abMed = if ($tempos.Count) { ($tempos | Sort-Object)[[int][math]::Floor($tempos.Count / 2)] } else { $null }
    $hand = "$(Num 'Handles_parado') / $(Num 'Handles_ciclo5') / $(Num 'Handles_ciclo20') / $(Num 'Handles_ciclo50')"
    $okM = ($null -ne $cpu -and $cpu -lt 0.1) -and ($null -ne $cres -and $cres -lt 5) -and ($null -ne $cresB -and $cresB -lt 3) -and ($null -ne $abMax -and $abMax -lt 300)
    Resultado '5.9 Medicao' ([bool]$okM) "CPU parada=$cpu% (< 0,1) WS privado 5 a 20=$cres MB (< 5) bytes privados 20 a 50=$cresB MB (< 3) abertura: mediana=$abMed ms maxima=$abMax ms em $($tempos.Count) aberturas (< 300) handles parado/5/20/50=$hand"
    ($saidaM -split "`r?`n") | Where-Object { $_.Trim() } | ForEach-Object { "     $_" }
  } else { Pulado '5.9 Medicao' 'parametro -SemMedicao' }
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
if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $falhas++ }
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
